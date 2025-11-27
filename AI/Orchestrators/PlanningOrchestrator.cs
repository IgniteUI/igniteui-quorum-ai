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
    public class PlanningOrchestrator(ExecutorConfig config, IAIClient planningClient, IAIClient planVotingClient)
    {
        #region Events
        public Action<IList<Step>, IList<Step>> OnStepsAccepted { get; set; } = delegate { };
        public Action<IList<Step>> OnStepsProposed { get; set; } = delegate { };
        public Action<VoteState> OnVoteChanged { get; set; } = delegate { };
        public Action<AIVoteException> OnStepsRejected { get; set; } = delegate { };
        #endregion

        public List<IAIRedFlagValidator> DefaultPlanningValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(100),
        ];

        protected static List<IAIRedFlagValidator> VoteValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(2),
        ];

        public int MaxRetries { get; set; } = 5;

        public async Task<IList<Step>> Plan(string prompt, string format = "plaintext", int batchSize = 2, int k = 10, IList<Step> prependSteps = null!, List<IAIRedFlagValidator> validators = null!)
        {
            var step = string.Empty;
            AIVoteException lastRejection = null!;
            int votingRetryCount = 0;

            if (string.IsNullOrEmpty(prompt))
            {
                throw new ArgumentNullException($"{nameof(prompt)} must be a non-empty string");
            }

            var steps = new List<Step>();
            if (prependSteps != null && prependSteps.Count > 0)
            {
                steps.AddRange(prependSteps);
            }

            while (step != "End")
            {
                try
                {
                    var proposedSteps = await PlanInternal(prompt, steps, batchSize, format, k, validators, lastRejection!);
                    OnStepsAccepted?.Invoke(proposedSteps, [.. steps]);

                    foreach (var stepObj in proposedSteps)
                    {
                        step = stepObj.Task;
                        if (!string.IsNullOrEmpty(step) && step != "End")
                        {
                            steps.Add(stepObj);
                        }

                        if (step == "End")
                        {
                            break;
                        }
                    }
                    
                    // Reset retry count on successful planning
                    votingRetryCount = 0;
                }
                catch (AIVoteException ex)
                {
                    if (ex.Reason == VoteCancellationReason.Rejected)
                    {
                        lastRejection = ex;
                        votingRetryCount++;
                        
                        // If max retries exceeded, restart planning from scratch
                        if (votingRetryCount >= MaxRetries)
                        {
                            steps.Clear();
                            if (prependSteps != null && prependSteps.Count > 0)
                            {
                                steps.AddRange(prependSteps);
                            }
                            lastRejection = null!;
                            votingRetryCount = 0;
                            step = string.Empty;
                        }
                    }
                    continue;
                }
                lastRejection = null!;
            }

            return steps;
        }

        public async Task<List<Step>> PlanInternal(string task, IEnumerable<Step> steps, int batchSize = 2, string format = "plaintext", int k = 5, List<IAIRedFlagValidator> validators = null!, AIVoteException lastRejection = null!)
        {
            var planTemplate = await ReadPromptTemplate(config.Instructions.Plan);
            var planFormat = await ReadPromptTemplate(config.Instructions.PlanFormat);
            var rules = await ReadPromptTemplate(config.Instructions.PlanRules);

            var prompt = planTemplate
                .Replace("{TASK}", task)
                .Replace("{STEPS}", string.Join(Environment.NewLine, steps.Select(s => "- " + s.Task)))
                .Replace("{RULES}", rules)
                .Replace("{OUTPUT_FORMAT}", format)
                .Replace("{PLAN_FORMAT}", planFormat);

            rules = rules.Replace("{BATCH_SIZE}", batchSize.ToString());

            if (lastRejection != null)
            {
                var rejectedSteps = string.Join(Environment.NewLine, lastRejection.ProposedSteps.Select(s => " - " + s.Task));
                var rejectionReasons = string.Join(Environment.NewLine, lastRejection.RejectionReasons.Select(r => "- " + r));
                prompt = prompt.Replace("{LAST_REJECTION}", $"The last rejected Steps were:{Environment.NewLine}{rejectedSteps}{Environment.NewLine}Reasons:{Environment.NewLine}{rejectionReasons}");
            }

            prompt = ClearUnusedTemplateVariables(prompt);

            validators ??= DefaultPlanningValidators;

            var response = await GuardedRequest(prompt, planningClient, validators);
            if (response == "End")
            {
                response = JsonSerializer.Serialize(new List<Step>() { new() { Task = "End", RequiredSteps = [] } });
            }

            var deserializedSteps = new List<Step>();

            try
            {
                deserializedSteps = JsonSerializer.Deserialize<List<Step>>(response);
            }
            catch (JsonException)
            {
                try
                {
                    var singleStep = JsonSerializer.Deserialize<Step>(response);
                    if (singleStep != null)
                    {
                        deserializedSteps!.Add(singleStep);
                    }
                    else
                    {
                        throw new AIRedFlagException("Invalid Step format.");
                    }
                }
                catch (JsonException)
                {
                    throw new AIRedFlagException("Invalid Step format.");
                }
            }

            if (deserializedSteps == null || deserializedSteps.Count == 0)
            {
                throw new AIRedFlagException("Deserialized plan is empty.");
            }

            try
            {
                OnStepsProposed?.Invoke(deserializedSteps);
                var (vote, reasons) = await VotePlanInternal(task, deserializedSteps, steps, k);
                if (!vote)
                {
                    var rejection = new AIVoteException($"Proposed step was rejected by voting.", deserializedSteps, reasons);
                    OnStepsRejected?.Invoke(rejection);
                    throw rejection;
                }
            }
            catch (AIVoteException ex)
            {
                if (ex.Reason == VoteCancellationReason.Ended)
                {
                    return [new() { Task = "End", RequiredSteps = [] }];
                }
                else
                {
                    throw;
                }
            }

            return deserializedSteps;
        }

        public async Task<(bool, IEnumerable<string>)> VotePlanInternal(string task, IEnumerable<Step> proposed, IEnumerable<Step> steps, int k = 5)
        {
            var voteTemplate = await ReadPromptTemplate(config.Instructions.PlanVote);
            var planFormat = await ReadPromptTemplate(config.Instructions.PlanFormat);
            var rules = await ReadPromptTemplate(config.Instructions.PlanRules);

            var prompt = voteTemplate
                .Replace("{TASK}", task)
                .Replace("{STEP}", string.Join(Environment.NewLine, proposed.Select(s => $"  <step>{s.Task}</step>")))
                .Replace("{STEPS}", string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace("{RULES}", rules)
                .Replace("{PLAN_FORMAT}", planFormat);

            prompt = ClearUnusedTemplateVariables(prompt);

            try
            {
                var (vote, reasons) = await this.RunVotingRound(k, prompt, planVotingClient);
                return (vote, reasons);
            }
            catch
            {
                var (vote, reasons) = await this.RunVotingRound(k, prompt, planVotingClient);
                return (vote, reasons);
            }
        }

        private async Task<(bool, IEnumerable<string>)> RunVotingRound(int k, string prompt, IAIClient client)
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

                var votes = GenerateVoteRequests(prompt, k, client);
                foreach (var bucket in TaskUtils.Interleaved(votes))
                {
                    var t = await bucket;
                    var voteResponse = await t;
                    voteResponse = voteResponse.ReplaceLineEndings().Trim();

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
                            votes.AddRange(GenerateVoteRequests(prompt, 1, client));
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

        private List<Task<string>> GenerateVoteRequests(string prompt, int amount, IAIClient client)
        {
            var output = new List<Task<string>>();
            for (int i = 0; i < amount; i++)
            {
                output.Add(GuardedRequest(prompt, client, VoteValidators));
            }

            return output;
        }

        private async Task<string> GuardedRequest(string prompt, IAIClient client, List<IAIRedFlagValidator> validators)
        {
            try
            {
                var response = await client.Request(prompt) ?? throw new AIRedFlagException("Received null response from the model.");

                var jsonMatch = Regex.Match(response, @"```json\s*(.*?)\s*```", RegexOptions.Singleline);
                response = jsonMatch.Success ? jsonMatch.Groups[1].Value.Trim() : response.Trim();

                validators.ForEach(validator => validator.Validate(response));

                return response;
            }
            catch (AIRedFlagException ex)
            {
                return await GuardedRequest($"{prompt}\n\nLast response was rejected:\n{ex.Message}", client, validators);
            }
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
