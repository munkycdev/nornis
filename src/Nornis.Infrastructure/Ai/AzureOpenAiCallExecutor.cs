using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The one shell around a chat completion: timeout enforcement, usage capture, and the
/// translation of SDK failures into the Application taxonomy (timeout / parse / HTTP).
/// Nine clients used to paste this preamble with three drifted exception vocabularies;
/// each client now contributes only its prompt, schema, and parse.
/// </summary>
public static class AzureOpenAiCallExecutor
{
    public static Task<(string Content, AiUsage Usage)> ExecuteAsync(
        ChatClient chatClient,
        AiPromptRequest request,
        ChatCompletionOptions completionOptions,
        string operation,
        ILogger logger,
        CancellationToken ct) =>
        ExecuteAsync(
            chatClient,
            [new SystemChatMessage(request.SystemPrompt), new UserChatMessage(request.UserMessage)],
            completionOptions, request.Model, request.TimeoutSeconds, operation, logger, ct);

    /// <summary>Message-list overload for the vision clients, whose user message carries image parts.</summary>
    public static async Task<(string Content, AiUsage Usage)> ExecuteAsync(
        ChatClient chatClient,
        List<ChatMessage> messages,
        ChatCompletionOptions completionOptions,
        string model,
        int timeoutSeconds,
        string operation,
        ILogger logger,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, completionOptions, linkedCts.Token);
            stopwatch.Stop();

            var completion = response.Value;
            var usage = completion.Usage;

            return (completion.Content[0].Text, new AiUsage
            {
                Model = model,
                InputTokens = usage.InputTokenCount,
                CachedInputTokens = usage.InputTokenDetails?.CachedTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
            });
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("{Operation} AI call timed out after {TimeoutSeconds}s", operation, timeoutSeconds);
            throw new AiTimeoutException($"{operation} AI call timed out after {timeoutSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            logger.LogError(ex, "{Operation} AI call failed with status {Status}", operation, ex.Status);
            throw new AiHttpException($"{operation} AI call failed: HTTP {ex.Status}", ex.Status, ex);
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled; nothing to translate.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during {Operation} AI call", operation);
            throw new AiHttpException($"Unexpected error during {operation} AI call.", null, ex);
        }
    }
}
