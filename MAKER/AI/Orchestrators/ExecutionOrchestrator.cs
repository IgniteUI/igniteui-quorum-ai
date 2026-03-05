using MAKER.AI.Clients;
using MAKER.AI.Constants;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Validation;
using MAKER.Configuration;
using System.Diagnostics;
using System.Text.Json;

namespace MAKER.AI.Orchestrators
{
    public sealed class ExecutionOrchestrator(ExecutorConfig config, IAIClient executionClient, IAIClient executionVotingClient) : OrchestratorBase
    {

        #region Events
        public event Action<IList<Step>, IList<Step>>? OnExecutionStarted;
        public event Action<string>? OnStateChanged;
        #endregion

        public async Task<string> Execute(IList<Step> steps, string prompt, string format, int batchSize = 2, int k = 10, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default)
        {
            var completedSteps = new List<Step>();
            var stepsList = steps.ToList();
            var state = string.Empty;

            if (stepsList.Count > 0)
            {
                var totalBatches = (int)Math.Ceiling(stepsList.Count / (double)batchSize);

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batchSteps = stepsList.Skip(batchIndex * batchSize).Take(batchSize).ToList();

                    OnExecutionStarted?.Invoke(batchSteps, completedSteps);
                    state = await ExecuteSteps(prompt, format, state, batchSteps, k, validators, tools, cancellationToken: cancellationToken);

                    OnStateChanged?.Invoke(state);

                    completedSteps.AddRange(batchSteps);
                }
            }

            return state;
        }

        internal async Task<string> ExecuteStepsInternal(string task, string state, string format, IEnumerable<Step> steps, int k = 5, List<IAIRedFlagValidator>? validators = null, object? tools = null, AIVoteException? lastRejection = null, CancellationToken cancellationToken = default)
        {
            var executionTemplate = await ReadPromptTemplate(config.Instructions.Execute, cancellationToken);
            var rules = await ReadPromptTemplate(config.Instructions.ExecuteRules, cancellationToken);

            var prompt = executionTemplate
                .Replace(TemplateVariables.Task, task)
                .Replace(TemplateVariables.Step, string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace(TemplateVariables.State, state)
                .Replace(TemplateVariables.Rules, rules);

            if (!string.IsNullOrEmpty(format) && steps.Any(s => s.RequiresFormat))
            {
                prompt = prompt.Replace(TemplateVariables.OutputFormat, "Required output format:" + Environment.NewLine + format);
            }

            if (steps.Any(s => !string.IsNullOrEmpty(s.ExtraContext)))
            {
                prompt = prompt.Replace(TemplateVariables.ExtraContext, string.Join(Environment.NewLine, steps.Select(s => s.ExtraContext)));
            }

            if (lastRejection != null)
            {
                var rejectionReasons = string.Join(Environment.NewLine, lastRejection.RejectionReasons.Select(r => "- " + r));
                prompt = prompt.Replace(TemplateVariables.LastRejection, $"The last rejected execution had the following reasons:{Environment.NewLine}{rejectionReasons}");
            }

            prompt = ClearUnusedTemplateVariables(prompt);

            var response = await executionClient.GuardedRequest(prompt, validators ?? [], tools, cancellationToken);
            if (string.IsNullOrEmpty(response.Content))
            {
                throw new AIRedFlagException("Execution client returned empty response.");
            }

            var (vote, reasons) = await VoteExecutionInternal(task, steps, response.Content, state, k, tools, cancellationToken);
            if (!vote)
            {
                throw new AIVoteException($"Proposed step was rejected by voting.", steps, reasons);
            }

            return response.Content;
        }

        internal async Task<(bool, IEnumerable<string>)> VoteExecutionInternal(string task, IEnumerable<Step> proposed, string state, string prevState, int k = 5, object? tools = null, CancellationToken cancellationToken = default)
        {
            var voteTemplate = await ReadPromptTemplate(config.Instructions.ExecuteVote, cancellationToken);
            var rules = await ReadPromptTemplate(config.Instructions.ExecuteRules, cancellationToken);

            var prompt = voteTemplate
                .Replace(TemplateVariables.Task, task)
                .Replace(TemplateVariables.Step, JsonSerializer.Serialize(proposed.Select(s => s.Task)))
                .Replace(TemplateVariables.State, state)
                .Replace(TemplateVariables.CurrentState, prevState)
                .Replace(TemplateVariables.Rules, rules);

            prompt = ClearUnusedTemplateVariables(prompt);

            var (vote, reasons, _) = await RunVotingRound(k, prompt, executionVotingClient, tools, cancellationToken);
            return (vote, reasons);
        }

        private async Task<string> ExecuteSteps(string prompt, string format, string state, IEnumerable<Step> steps, int k, List<IAIRedFlagValidator>? validators = null, object? tools = null, AIVoteException? lastRejection = null, CancellationToken cancellationToken = default)
        {
            int votingRetryCount = 0;
            var currentState = state;

            while (votingRetryCount < MaxRetries)
            {
                try
                {
                    return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, tools, lastRejection, cancellationToken);
                }
                catch (AIVoteException ex)
                {
                    lastRejection = ex;
                    votingRetryCount++;

                    // If max retries exceeded, reset state and retry from scratch
                    if (votingRetryCount >= MaxRetries)
                    {
                        lastRejection = null;
                        currentState = string.Empty;
                        return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, tools, null, cancellationToken);
                    }
                }
            }

            throw new UnreachableException();
        }
    }
}
