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
    private readonly OpenAIClient? _client;
    private readonly string? _unconfigured;
    private readonly ConcurrentDictionary<string, IChatClient> _byDeployment = new(StringComparer.Ordinal);

    private NornisChatClients(OpenAIClient? client, string? unconfigured)
    {
        _client = client;
        _unconfigured = unconfigured;
    }

    public static NornisChatClients Create(OpenAIClient client) => new(client, null);

    /// <summary>
    /// A host with no Azure OpenAI of its own: a call Nornis would pay for fails with
    /// <paramref name="message"/>, while calls on users' keys, cover art among them (feature 38),
    /// still bind.
    /// </summary>
    public static NornisChatClients Unconfigured(string message) => new(null, message);

    public IChatClient For(string deployment) => _client is null
        ? throw new InvalidOperationException(_unconfigured)
        : _byDeployment.GetOrAdd(deployment, d => new AzureOpenAiChatClient(_client.GetChatClient(d)));
}
