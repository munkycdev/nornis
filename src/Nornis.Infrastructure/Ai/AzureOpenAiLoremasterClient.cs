using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiLoremasterClient : ILoremasterAiClient
{
    private readonly ChatClient _chatClient;
    private readonly LoremasterOptions _options;
    private readonly ILogger<AzureOpenAiLoremasterClient> _logger;

    public AzureOpenAiLoremasterClient(
        ChatClient chatClient,
        IOptions<LoremasterOptions> options,
        ILogger<AzureOpenAiLoremasterClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LoremasterAiResponse> AskAsync(LoremasterAiRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(request.SystemPrompt),
                new UserChatMessage(request.UserMessage)
            };

            var response = await _chatClient.CompleteChatAsync(
                messages,
                cancellationToken: linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var answerText = chatCompletion.Content[0].Text;
            var usage = chatCompletion.Usage;

            return new LoremasterAiResponse
            {
                AnswerText = answerText,
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Model = request.Model
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Loremaster AI call timed out after {TimeoutSeconds}s",
                request.TimeoutSeconds);

            throw new AiLoremasterTimeoutException(
                $"Loremaster AI call timed out after {request.TimeoutSeconds} seconds.",
                (int)stopwatch.ElapsedMilliseconds);
        }
        catch (ClientResultException ex) when (ex.Status == (int)HttpStatusCode.TooManyRequests)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Loremaster AI rate limited (429)");

            throw new AiLoremasterRateLimitException(
                "AI service rate limited. Please try again later.",
                (int)stopwatch.ElapsedMilliseconds,
                ex);
        }
        catch (ClientResultException ex) when (IsServerError(ex.Status))
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Loremaster AI service error (status {Status})", ex.Status);

            throw new AiLoremasterServiceException(
                $"AI service error: HTTP {ex.Status}",
                ex.Status,
                (int)stopwatch.ElapsedMilliseconds,
                ex);
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Loremaster AI call failed with status {Status}", ex.Status);

            throw new AiLoremasterServiceException(
                $"AI call failed: HTTP {ex.Status}",
                ex.Status,
                (int)stopwatch.ElapsedMilliseconds,
                ex);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error during Loremaster AI call");

            throw new AiLoremasterServiceException(
                "Unexpected error during Loremaster AI call.",
                500,
                (int)stopwatch.ElapsedMilliseconds,
                ex);
        }
    }

    private static bool IsServerError(int statusCode) => statusCode >= 500 && statusCode < 600;
}
