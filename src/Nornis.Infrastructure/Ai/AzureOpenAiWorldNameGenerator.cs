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
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

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

            var options = new ChatCompletionOptions { MaxOutputTokenCount = 20, Temperature = 1.2f };
            var response = await _chatClient.CompleteChatAsync(messages, options, linkedCts.Token);

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
