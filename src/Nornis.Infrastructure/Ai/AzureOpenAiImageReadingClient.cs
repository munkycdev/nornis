using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IImageReadingClient"/>. Shares the
/// extraction <see cref="ChatClient"/> (nornis-extract, gpt-5.4 — multimodal); sends one
/// user message with a text part naming the files followed by one image part per file.
/// Plain markdown out — the extraction pass downstream does the structuring.
/// </summary>
public class AzureOpenAiImageReadingClient : IImageReadingClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiImageReadingClient> _logger;

    public AzureOpenAiImageReadingClient(
        ChatClient chatClient, ILogger<AzureOpenAiImageReadingClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImageReadingResponse> ReadAsync(ImageReadingRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var fileNames = string.Join(", ", request.Images.Select(i => i.FileName));
            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(
                    $"Read the following {request.Images.Count} image(s), in order: {fileNames}.")
            };
            foreach (var image in request.Images)
            {
                parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(image.ImageBytes), image.MediaType));
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

            return new ImageReadingResponse
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
            _logger.LogWarning("Image reading timed out after {TimeoutSeconds}s", request.TimeoutSeconds);
            throw new TimeoutException($"Image reading timed out after {request.TimeoutSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Image reading failed with status {Status}", ex.Status);
            throw new HttpRequestException($"Image reading failed: HTTP {ex.Status}", ex, (HttpStatusCode)ex.Status);
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
            _logger.LogError(ex, "Unexpected error during image reading");
            throw new HttpRequestException("Unexpected error during image reading.", ex);
        }
    }

    internal static string BuildSystemPrompt()
    {
        return """
            You read images for Nornis, a tabletop-RPG world memory system. You receive one
            or more images attached to a source — artwork, screenshots, photographed
            handouts, diagrams, or scanned documents.

            For each image, produce a markdown section headed "## {filename}" containing:
            - Any legible text in the image, transcribed faithfully.
            - A concise description of what the image depicts, focused on lore: named
              people, places, factions, items, events, symbols, and their visible
              relationships.
            - Proper nouns matter most. Mark uncertain readings like [?Voss]; skip pure
              decoration.

            Do not invent names or facts that are not visibly supported. Return ONLY the
            markdown sections — no preamble, no commentary.
            """;
    }
}
