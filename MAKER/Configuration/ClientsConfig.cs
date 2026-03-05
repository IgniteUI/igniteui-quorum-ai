namespace MAKER.Configuration
{
    public class ClientsConfig
    {
        public required ClientProviderConfig Planning { get; set; }
        public required ClientProviderConfig PlanVoting { get; set; }
        public required ClientProviderConfig Execution { get; set; }
        public required ClientProviderConfig ExecutionVoting { get; set; }
    }
}
