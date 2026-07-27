using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IWorldNameGenerator"/>, reusing the
/// Loremaster's <see cref="ChatClient"/>. One tiny completion with a hard internal
/// timeout; any failure returns null so the caller's static fallback names apply —
/// naming must never block or fail demo world creation.
/// </summary>
public class AzureOpenAiWorldNameGenerator : IWorldNameGenerator
{
    // Generous enough for a cold Azure OpenAI call — both prod fallback names observed on
    // 2026-07-25/26 were plausibly 3s timeouts. Import dominates creation time anyway.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiWorldNameGenerator> _logger;

    public AzureOpenAiWorldNameGenerator(ChatClient chatClient, ILogger<AzureOpenAiWorldNameGenerator> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string?> GenerateAsync(CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "Invent one original name for a fantasy tabletop campaign world. " +
                    "One to three words, evocative, no real-world or published-setting names. " +
                    "Reply with the name only — no quotes, no punctuation, no explanation."),
                new UserChatMessage("Name the world."),
            };

            // This call has been failing in production since at least 2026-07-26 and nobody
            // noticed, because the catch below turns any failure into the static-name fallback.
            // The cause was the same HTTP 400 that later took every other AI feature down: the
            // SDK serialises MaxOutputTokenCount as "max_tokens", which these deployments reject
            // (see AzureOpenAiExtractionClient). Temperature goes with it — this model family
            // rejects a non-default value the same way, and it would simply be the next 400.
            //
            // Both were cosmetic here: the system prompt already asks for one to three words.
            var response = await _chatClient.CompleteChatAsync(messages, options: null, linkedCts.Token);

            var name = response.Value.Content.Count > 0 ? response.Value.Content[0].Text?.Trim() : null;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim('"', '\'', '.');
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "World name generation failed");
            return null;
        }
    }
}
