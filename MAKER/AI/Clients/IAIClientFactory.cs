using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    public interface IAIClientFactory
    {
        IAIClient CreateClient(ClientProviderConfig config);
    }
}
