using MAKER.AI.Clients;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Redflag;
using MAKER.Configuration;
using MAKER.Utils;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MAKER.AI.Orchestrators
{
    public class ExecutionOrchestrator(ExecutorConfig config, AIClientBase executionClient, AIClientBase executionVotingClient)
    {

        #region Events
        public Action<IList<Step>, IList<Step>> OnExecutionStarted { get; set; } = delegate { };
        public Action<string> OnStateChanged { get; set; } = delegate { };
        public Action<VoteState> OnVoteChanged { get; set; } = delegate { };
        #endregion

        public int MaxRetries { get; set; } = 5;

        protected static List<IAIRedFlagValidator> VoteValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(2),
        ];

        public async Task<string> Execute(IList<Step> steps, string prompt, string format, int batchSize = 2, int k = 10, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            var completedSteps = new List<Step>();
            var stepsList = steps.ToList();
            var state = string.Empty;

            if (stepsList.Count > 0)
            {
                var totalBatches = (int)Math.Ceiling(stepsList.Count / (double)batchSize);

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    var batchSteps = stepsList.Skip(batchIndex * batchSize).Take(batchSize).ToList();

                    OnExecutionStarted?.Invoke(batchSteps, completedSteps);
                    state = await ExecuteSteps(prompt, format, state, batchSteps, k, validators, tools);

                    OnStateChanged?.Invoke(state);

                    completedSteps.AddRange(batchSteps);
                }
            }

            return state;
        }

        public async Task<string> ExecuteStepsInternal(string task, string state, string format, IEnumerable<Step> steps, int k = 5, List<IAIRedFlagValidator> validators = null!, object? tools = null!, AIVoteException lastRejection = null!)
        {
            var executionTemplate = await ReadPromptTemplate(config.Instructions.Execute);
            var rules = await ReadPromptTemplate(config.Instructions.ExecuteRules);

            var prompt = executionTemplate
                .Replace("{TASK}", task)
                .Replace("{STEP}", string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace("{STATE}", state)
                .Replace("{RULES}", rules);

            if (!string.IsNullOrEmpty(format) && steps.Any(s => s.RequiresFormat))
            {
                prompt = prompt.Replace("{OUTPUT_FORMAT}", "Required output format:" + Environment.NewLine + format);
            }

            if (steps.Any(s => !string.IsNullOrEmpty(s.ExtraContext)))
            {
                prompt = prompt.Replace("{EXTRA_CONTEXT}", string.Join(Environment.NewLine, steps.Select(s => s.ExtraContext)));
            }

            if (lastRejection != null)
            {
                var rejectionReasons = string.Join(Environment.NewLine, lastRejection.RejectionReasons.Select(r => "- " + r));
                prompt = prompt.Replace("{LAST_REJECTION}", $"The last rejected execution had the following reasons:{Environment.NewLine}{rejectionReasons}");
            }

            prompt = ClearUnusedTemplateVariables(prompt);

            var response = await executionClient.GuardedRequest(prompt, validators ?? [], tools);
            if (string.IsNullOrEmpty(response.Content))
            {
                throw new AIRedFlagException("Execution client returned empty response.");
            }

            var (vote, reasons) = await VoteExecutionInternal(task, steps, response.Content, state, k, tools);
            if (!vote)
            {
                throw new AIVoteException($"Proposed step was rejected by voting.", steps, reasons);
            }

            return response.Content;
        }

        public async Task<(bool, IEnumerable<string>)> VoteExecutionInternal(string task, IEnumerable<Step> proposed, string state, string prevState, int k = 5, object? tools = null!)
        {
            var voteTemplate = await ReadPromptTemplate(config.Instructions.ExecuteVote);
            var rules = await ReadPromptTemplate(config.Instructions.ExecuteRules);

            var prompt = voteTemplate
                .Replace("{TASK}", task)
                .Replace("{STEP}", JsonSerializer.Serialize(proposed.Select(s => s.Task)))
                .Replace("{STATE}", state)
                .Replace("{CURRENT_STATE}", prevState)
                .Replace("{RULES}", rules);

            prompt = ClearUnusedTemplateVariables(prompt);

            try
            {
                var (vote, reasons) = await this.RunVotingRound(k, prompt, executionVotingClient, tools);
                return (vote, reasons);
            }
            catch
            {
                var (vote, reasons) = await this.RunVotingRound(k, prompt, executionVotingClient, tools);
                return (vote, reasons);
            }
        }

        private async Task<string> ExecuteSteps(string prompt, string format, string state, IEnumerable<Step> steps, int k, List<IAIRedFlagValidator> validators = null!, object? tools = null!, AIVoteException lastRejection = null!)
        {
            int votingRetryCount = 0;
            var currentState = state;
            
            while (votingRetryCount < MaxRetries)
            {
                try
                {
                    return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, tools, lastRejection);
                }
                catch (AIVoteException ex)
                {
                    lastRejection = ex;
                    votingRetryCount++;

                    // If max retries exceeded, reset state and retry from scratch
                    if (votingRetryCount >= MaxRetries)
                    {
                        lastRejection = null!;
                        currentState = string.Empty;
                        return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, tools, null!);
                    }
                }
            }
            
            // This should not be reached due to the final attempt above, but included for safety
            return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, lastRejection);
        }

        private async Task<(bool, IEnumerable<string>)> RunVotingRound(int k, string prompt, IAIClient client, object? tools = null!)
        {
            int positive = 0;
            int negative = 0;
            int end = 0;

            var reasons = new List<string>();

            while (positive < negative + k && negative < positive + k && end != k)
            {
                end = 0;
                if (positive + negative >= k * 4)
                {
                    throw new AIVoteException("Voting round exceeded maximum number of votes without reaching consensus.", VoteCancellationReason.Contentious);
                }

                var votes = GenerateVoteRequests(prompt, k, client, tools);
                foreach (var bucket in TaskUtils.Interleaved(votes))
                {
                    var t = await bucket;
                    var voteResponseObj = await t;
                    var voteResponse = voteResponseObj.Content!.ReplaceLineEndings().Trim();

                    if (voteResponse != null)
                    {
                        if (voteResponse == "Yes")
                        {
                            positive++;
                        }
                        else if (voteResponse.StartsWith("No"))
                        {
                            negative++;
                            var resp = voteResponse.ReplaceLineEndings().Split(Environment.NewLine);
                            if (resp.Length > 1)
                            {
                                reasons.Add(string.Join(Environment.NewLine, resp.Skip(1)));
                            }
                        }
                        else if (voteResponse == "End")
                        {
                            end++;
                        }
                        else
                        {
                            // Some other non-value
                            votes.AddRange(GenerateVoteRequests(prompt, 1, client, tools));
                            continue;
                        }

                        OnVoteChanged?.Invoke(new()
                        {
                            KValue = k,
                            Votes = {
                                { "Yes", positive },
                                { "No", negative },
                                { "End", end }
                            }
                        });
                        if (positive >= negative + k || negative >= positive + k || end == k)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // Handle unexpected response
                        throw new Exception("Unexpected response from vote.");
                    }
                }
            }

            if (end == k)
            {
                throw new AIVoteException("Voters deemed the task finished.", VoteCancellationReason.Ended);
            }

            return (positive >= negative + k, reasons);
        }

        private List<Task<AIResponse>> GenerateVoteRequests(string prompt, int amount, IAIClient client, object? tools = null!)
        {
            var output = new List<Task<AIResponse>>();
            for (int i = 0; i < amount; i++)
            {
                output.Add(client.GuardedRequest(prompt, VoteValidators, tools));
            }

            return output;
        }

        private async Task<string> ReadPromptTemplate(string path)
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException($"Prompt template file not found: {path}");
            }
            return await File.ReadAllTextAsync(path);
        }

        private string ClearUnusedTemplateVariables(string prompt)
        {
            var regex = new Regex(@"\{[A-Z_]+\}");
            return regex.Replace(prompt, string.Empty);
        }
    }
}
