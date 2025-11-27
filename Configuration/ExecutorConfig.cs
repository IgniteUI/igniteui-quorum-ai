using Microsoft.Extensions.Configuration;

namespace MAKER.Configuration
{
    public class ExecutorConfig
    {
        public required AIProviderKeysConfig AIProviderKeys { get; set; }
        public required ClientsConfig Clients { get; set; }
        public required InstructionsConfig Instructions { get; set; }

        public static ExecutorConfig FromConfiguration(IConfigurationSection section)
        {
            var aiKeys = new AIProviderKeysConfig
            {
                Google = section["AIProviderKeys:Google"],
                OpenAI = section["AIProviderKeys:OpenAI"],
                Anthropic = section["AIProviderKeys:Anthropic"]
            };

            var clients = new ClientsConfig
            {
                Planning = new ClientProviderConfig
                {
                    Provider = section["Clients:Planning:Provider"] ?? throw new InvalidOperationException("Planning Provider is required"),
                    Model = section["Clients:Planning:Model"] ?? throw new InvalidOperationException("Planning Model is required")
                },
                PlanVoting = new ClientProviderConfig
                {
                    Provider = section["Clients:PlanVoting:Provider"] ?? throw new InvalidOperationException("PlanVoting Provider is required"),
                    Model = section["Clients:PlanVoting:Model"] ?? throw new InvalidOperationException("PlanVoting Model is required")
                },
                Execution = new ClientProviderConfig
                {
                    Provider = section["Clients:Execution:Provider"] ?? throw new InvalidOperationException("Execution Provider is required"),
                    Model = section["Clients:Execution:Model"] ?? throw new InvalidOperationException("Execution Model is required")
                },
                ExecutionVoting = new ClientProviderConfig
                {
                    Provider = section["Clients:ExecutionVoting:Provider"] ?? throw new InvalidOperationException("ExecutionVoting Provider is required"),
                    Model = section["Clients:ExecutionVoting:Model"] ?? throw new InvalidOperationException("ExecutionVoting Model is required")
                }
            };

            var instructions = new InstructionsConfig
            {
                Plan = section["Instructions:Plan"] ?? throw new InvalidOperationException("Plan instruction path is required"),
                PlanVote = section["Instructions:PlanVote"] ?? throw new InvalidOperationException("PlanVote instruction path is required"),
                PlanRules = section["Instructions:PlanRules"] ?? throw new InvalidOperationException("PlanRules instruction path is required"),
                PlanFormat = section["Instructions:PlanFormat"] ?? throw new InvalidOperationException("PlanFormat instruction path is required"),
                Execute = section["Instructions:Execute"] ?? throw new InvalidOperationException("Execute instruction path is required"),
                ExecuteVote = section["Instructions:ExecuteVote"] ?? throw new InvalidOperationException("ExecuteVote instruction path is required"),
                ExecuteRules = section["Instructions:ExecuteRules"] ?? throw new InvalidOperationException("ExecuteRules instruction path is required")
            };

            return new ExecutorConfig
            {
                AIProviderKeys = aiKeys,
                Clients = clients,
                Instructions = instructions
            };
        }
    }

    public class AIProviderKeysConfig
    {
        public string? Google { get; set; }
        public string? OpenAI { get; set; }
        public string? Anthropic { get; set; }
    }

    public class ClientsConfig
    {
        public required ClientProviderConfig Planning { get; set; }
        public required ClientProviderConfig PlanVoting { get; set; }
        public required ClientProviderConfig Execution { get; set; }
        public required ClientProviderConfig ExecutionVoting { get; set; }
    }

    public class ClientProviderConfig
    {
        public required string Provider { get; set; }
        public required string Model { get; set; }
    }

    public class InstructionsConfig
    {
        public required string Plan { get; set; }
        public required string PlanVote { get; set; }
        public required string PlanRules { get; set; }
        public required string PlanFormat { get; set; }
        public required string Execute { get; set; }
        public required string ExecuteVote { get; set; }
        public required string ExecuteRules { get; set; }
    }
}
