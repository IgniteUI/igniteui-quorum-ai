using MAKER.AI.Clients;
using MAKER.AI.Constants;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Validation;
using MAKER.Configuration;
using MAKER.Utils;
using System.Text.Json;

namespace MAKER.AI.Orchestrators
{
    public sealed class PlanningOrchestrator(ExecutorConfig config, IAIClient planningClient, IAIClient planVotingClient) : OrchestratorBase
    {
        #region Events
        public event Action<IList<Step>, IList<Step>>? OnStepsAccepted;
        public event Action<IList<Step>>? OnStepsProposed;
        public event Action<AIVoteException>? OnStepsRejected;
        #endregion

        public List<IAIRedFlagValidator> DefaultPlanningValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(100),
        ];

        public async Task<IList<Step>> Plan(string prompt, string format = "plaintext", int batchSize = 3, int k = 10, int maxSteps = 20, IList<Step>? prependSteps = null, List<IAIRedFlagValidator>? validators = null, object? tools = null, List<MCPServerInfo>? mcpServers = null, CancellationToken cancellationToken = default)
        {
            var step = string.Empty;
            AIVoteException? lastRejection = null;
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

            while (step != AIResponses.End)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var proposedSteps = await PlanInternal(prompt, steps, batchSize, format, k, maxSteps, validators, lastRejection!, tools, mcpServers, cancellationToken);

                    foreach (var stepObj in proposedSteps)
                    {
                        step = stepObj.Task;
                        if (!string.IsNullOrEmpty(step) && step != AIResponses.End)
                        {
                            steps.Add(stepObj);
                        }

                        if (step == AIResponses.End)
                        {
                            break;
                        }
                    }

                    OnStepsAccepted?.Invoke(proposedSteps, steps);
                    // Reset retry count on successful planning
                    
                    votingRetryCount = 0;
                }
                catch (AIVoteException ex)
                {
                    if (ex.Reason == VoteCancellationReason.Rejected)
                    {
                        var accumulatedReasons = lastRejection != null
                            ? lastRejection.RejectionReasons.Concat(ex.RejectionReasons)
                            : ex.RejectionReasons;
                        lastRejection = new AIVoteException(ex.Message, ex.ProposedSteps, accumulatedReasons);
                        votingRetryCount++;

                        // If max retries exceeded, restart planning from scratch
                        if (votingRetryCount >= MaxRetries)
                        {
                            steps.Clear();
                            if (prependSteps != null && prependSteps.Count > 0)
                            {
                                steps.AddRange(prependSteps);
                            }
                            lastRejection = null;
                            votingRetryCount = 0;
                            step = string.Empty;
                        }
                    }
                    continue;
                }
                catch (AIRedFlagException ex)
                {
                    var accumulatedReasons = lastRejection != null
                        ? lastRejection.RejectionReasons.Concat([ex.Message])
                        : [ex.Message];
                    lastRejection = new AIVoteException(ex.Message, [], accumulatedReasons);
                    continue;
                }
                lastRejection = null;
            }

            return steps;
        }

        internal async Task<List<Step>> PlanInternal(string task, IEnumerable<Step> steps, int batchSize = 2, string format = "plaintext", int k = 5, int maxSteps = 20, List<IAIRedFlagValidator>? validators = null, AIVoteException? lastRejection = null, object? tools = null, List<MCPServerInfo>? mcpServers = null, CancellationToken cancellationToken = default)
        {
            var planTemplate = await ReadPromptTemplate(config.Instructions.Plan, cancellationToken);
            var planFormat = await ReadPromptTemplate(config.Instructions.PlanFormat, cancellationToken);
            var rules = await ReadPromptTemplate(config.Instructions.PlanRules, cancellationToken);

            var prompt = planTemplate
                .Replace(TemplateVariables.Task, task)
                .Replace(TemplateVariables.Steps, string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace(TemplateVariables.PlanRules, rules)
                .Replace(TemplateVariables.OutputFormat, format)
                .Replace(TemplateVariables.PlanFormat, planFormat)
                .Replace(TemplateVariables.BatchSize, batchSize.ToString())
                .Replace(TemplateVariables.MaxSteps, maxSteps.ToString());

            if (lastRejection != null)
            {
                var rejectedSteps = string.Join(Environment.NewLine, lastRejection.ProposedSteps.Select(s => " - " + s.Task));
                var rejectionReasons = string.Join(Environment.NewLine, lastRejection.RejectionReasons.Select(r => "- " + r));
                prompt = prompt.Replace(TemplateVariables.LastRejection, $"The last rejected Steps were:{Environment.NewLine}{rejectedSteps}{Environment.NewLine}Reasons:{Environment.NewLine}{rejectionReasons}");
            }

            prompt = ClearUnusedTemplateVariables(prompt);

            validators ??= DefaultPlanningValidators;

            var responseObj = await planningClient.GuardedRequest(prompt, validators, [tools], mcpServers, cancellationToken);
            var response = responseObj.Content ?? throw new AIRedFlagException("Received null response from the model.");

            if (response == AIResponses.End)
            {
                response = JsonSerializer.Serialize(new List<Step>() { new() { Task = AIResponses.End, RequiredSteps = [] } });
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

            StepAtomicityValidator.ValidateAll(deserializedSteps);

            try
            {
                OnStepsProposed?.Invoke(deserializedSteps);
                var (vote, reasons, usage) = await VotePlanInternal(task, format, deserializedSteps, steps, batchSize, k, maxSteps, tools, mcpServers, cancellationToken);
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
                    return [new() { Task = AIResponses.End, RequiredSteps = [] }];
                }
                else
                {
                    throw;
                }
            }

            return deserializedSteps;
        }

        internal async Task<(bool, IEnumerable<string>, AIResponse)> VotePlanInternal(string task, string format, IEnumerable<Step> proposed, IEnumerable<Step> steps, int batchSize = 2, int k = 5, int maxSteps = 20, object? tools = null, List<MCPServerInfo>? mcpServers = null, CancellationToken cancellationToken = default)
        {
            var voteTemplate = await ReadPromptTemplate(config.Instructions.PlanVote, cancellationToken);
            var planFormat = await ReadPromptTemplate(config.Instructions.PlanFormat, cancellationToken);
            var rules = await ReadPromptTemplate(config.Instructions.PlanRules, cancellationToken);

            var prompt = voteTemplate
                .Replace(TemplateVariables.Task, task)
                .Replace(TemplateVariables.Step, string.Join(Environment.NewLine, proposed.Select(s => $"  <step>{s.Task}</step>")))
                .Replace(TemplateVariables.Steps, string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace(TemplateVariables.PlanRules, rules)
                .Replace(TemplateVariables.BatchSize, batchSize.ToString())
                .Replace(TemplateVariables.PlanFormat, planFormat)
                .Replace(TemplateVariables.MaxSteps, maxSteps.ToString());

            prompt = ClearUnusedTemplateVariables(prompt);
            var proposedIsEnd = proposed.Any(s => s.Task == AIResponses.End);
            try
            {
                return await RunVotingRound(k, prompt, planVotingClient, [new VoterTool(format), tools], mcpServers, treatEndAsYes: proposedIsEnd, cancellationToken: cancellationToken);
            }
            catch (AIVoteException ex)
            {
                if (ex.Reason == VoteCancellationReason.Contentious)
                {
                    return (false, [ex.Message], new AIResponse());
                }
                throw;
            }
        }
    }
}
