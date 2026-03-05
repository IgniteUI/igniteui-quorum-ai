using MAKER.AI.Clients;
using MAKER.AI.Constants;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Validation;
using MAKER.Utils;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MAKER.AI.Orchestrators
{
    public abstract partial class OrchestratorBase
    {
        public int MaxRetries { get; set; } = 5;

        public event Action<VoteState>? OnVoteChanged;

        protected List<IAIRedFlagValidator> VoteValidators { get; } =
        [
            new AIRedFlagMinLengthValidator(2),
        ];

        private static readonly ConcurrentDictionary<string, string> _templateCache = new();

        protected async Task<(bool Vote, IEnumerable<string> Reasons, AIResponse Usage)> RunVotingRound(int k, string prompt, IAIClient client, object? tools = null, CancellationToken cancellationToken = default)
        {
            int positive = 0;
            int negative = 0;
            int end = 0;

            var reasons = new List<string>();

            int inputTokens = 0;
            int outputTokens = 0;

            while (positive < negative + k && negative < positive + k && end != k)
            {
                end = 0;
                if (positive + negative >= k * 4)
                {
                    throw new AIVoteException("Voting round exceeded maximum number of votes without reaching consensus.", VoteCancellationReason.Contentious);
                }

                var votes = GenerateVoteRequests(prompt, k, client, tools, cancellationToken);
                foreach (var bucket in TaskUtils.Interleaved(votes))
                {
                    var t = await bucket;
                    var voteResponseObj = await t;

                    inputTokens += voteResponseObj.InputTokens;
                    outputTokens += voteResponseObj.OutputTokens;
                    var voteResponse = voteResponseObj.Content!.ReplaceLineEndings().Trim();

                    if (voteResponse != null)
                    {
                        if (voteResponse == AIResponses.Yes)
                        {
                            positive++;
                        }
                        else if (voteResponse.StartsWith(AIResponses.No))
                        {
                            negative++;
                            var resp = voteResponse.ReplaceLineEndings().Split(Environment.NewLine);
                            if (resp.Length > 1)
                            {
                                reasons.Add(string.Join(Environment.NewLine, resp.Skip(1)));
                            }
                        }
                        else if (voteResponse == AIResponses.End)
                        {
                            end++;
                        }
                        else
                        {
                            // Some other non-value
                            votes.AddRange(GenerateVoteRequests(prompt, 1, client, tools, cancellationToken));
                            continue;
                        }

                        OnVoteChanged?.Invoke(new()
                        {
                            KValue = k,
                            Votes = {
                                { AIResponses.Yes, positive },
                                { AIResponses.No, negative },
                                { AIResponses.End, end }
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
                        throw new InvalidOperationException("Unexpected response from vote.");
                    }
                }
            }

            if (end == k)
            {
                throw new AIVoteException("Voters deemed the task finished.", VoteCancellationReason.Ended);
            }

            return (positive >= negative + k, reasons, new AIResponse
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            });
        }

        protected List<Task<AIResponse>> GenerateVoteRequests(string prompt, int amount, IAIClient client, object? tools = null, CancellationToken cancellationToken = default)
        {
            var output = new List<Task<AIResponse>>();
            for (int i = 0; i < amount; i++)
            {
                output.Add(client.GuardedRequest(prompt, VoteValidators, tools, cancellationToken));
            }

            return output;
        }

        protected async Task<string> ReadPromptTemplate(string path, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Prompt template file not found: {fullPath}");
            }

            if (_templateCache.TryGetValue(fullPath, out var cached))
            {
                return cached;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            _templateCache.TryAdd(fullPath, content);
            return content;
        }

        [GeneratedRegex(@"\{[A-Z_]+\}")]
        private static partial Regex TemplateVariableRegex();

        protected string ClearUnusedTemplateVariables(string prompt)
        {
            return TemplateVariableRegex().Replace(prompt, string.Empty);
        }
    }
}
