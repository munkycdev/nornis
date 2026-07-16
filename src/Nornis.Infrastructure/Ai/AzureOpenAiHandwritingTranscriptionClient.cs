using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IHandwritingTranscriptionClient"/>. Shares
/// the extraction <see cref="ChatClient"/> (nornis-extract, gpt-5.4 — multimodal); sends
/// one user message with a text part followed by one image part per page, in order.
/// Plain-text response — transcription needs no structured output.
/// </summary>
public class AzureOpenAiHandwritingTranscriptionClient : IHandwritingTranscriptionClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiHandwritingTranscriptionClient> _logger;

    public AzureOpenAiHandwritingTranscriptionClient(
        ChatClient chatClient, ILogger<AzureOpenAiHandwritingTranscriptionClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HandwritingTranscriptionResponse> TranscribeAsync(HandwritingTranscriptionRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(
                    $"Transcribe the following {request.Pages.Count} page(s) of handwritten notes, in order.")
            };
            foreach (var page in request.Pages)
            {
                parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(page.ImageBytes), page.MediaType));
            }

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(BuildSystemPrompt()),
                new UserChatMessage(parts)
            };

            var response = await _chatClient.CompleteChatAsync(messages, options: null, linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var markdown = chatCompletion.Content.Count > 0 ? chatCompletion.Content[0].Text : string.Empty;
            var usage = chatCompletion.Usage;

            return new HandwritingTranscriptionResponse
            {
                Markdown = markdown,
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
            _logger.LogWarning("Handwriting transcription timed out after {TimeoutSeconds}s", request.TimeoutSeconds);
            throw new TimeoutException($"Handwriting transcription timed out after {request.TimeoutSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Handwriting transcription failed with status {Status}", ex.Status);
            throw new HttpRequestException($"Handwriting transcription failed: HTTP {ex.Status}", ex, (HttpStatusCode)ex.Status);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error during handwriting transcription");
            throw new HttpRequestException("Unexpected error during handwriting transcription.", ex);
        }
    }

    internal static string BuildSystemPrompt()
    {
        return """
            You transcribe handwritten tabletop-RPG session notes for Nornis, a world memory
            system. You receive photographed or scanned pages of handwriting, in reading order.

            Produce a faithful markdown transcription:
            - Transcribe exactly what is written. Do not summarize, reorder, expand, or invent.
            - Preserve the visible structure: headings, lists, indentation, emphasis
              (underlines become bold), tables if drawn.
            - Proper nouns matter most — names of people, places, factions, and things. When a
              word is genuinely unreadable, write [illegible]; when unsure between readings,
              pick the likelier and mark it like [?Voss].
            - Ignore doodles, margins scribbles that carry no words, and crossed-out text.
            - Join the pages into one continuous document; do not add page headers.

            Return ONLY the markdown transcription — no preamble, no commentary.
            """;
    }
}
