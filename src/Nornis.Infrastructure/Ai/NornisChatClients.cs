using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The chat clients for Nornis's own Azure OpenAI resource, one per deployment, built on first use
/// and kept for the life of the process (feature 39). The retry count is the host's: the Worker's
/// queue owns its retries, the API allows one (see <see cref="AzureOpenAiClientFactory"/>).
/// </summary>
public sealed class NornisChatClients
{
    private readonly OpenAIClient _client;
    private readonly ConcurrentDictionary<string, IChatClient> _byDeployment = new(StringComparer.Ordinal);

    private NornisChatClients(OpenAIClient client) => _client = client;

    public static NornisChatClients Create(OpenAIClient client) => new(client);

    public IChatClient For(string deployment) =>
        _byDeployment.GetOrAdd(deployment, d => new AzureOpenAiChatClient(_client.GetChatClient(d)));
}
