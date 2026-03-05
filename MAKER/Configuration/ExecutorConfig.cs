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
            var config = section.Get<ExecutorConfig>();
            if (config?.AIProviderKeys == null || config.Clients == null || config.Instructions == null)
            {
                throw new InvalidOperationException("MAKER configuration section is missing or incomplete. Ensure AIProviderKeys, Clients, and Instructions are configured.");
            }

            return config;
        }
    }
}
