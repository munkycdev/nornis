using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IMapExtractionClient"/>: the map image as
/// a vision part, existing Locations as matching context, and strict JSON-schema output
/// of labeled places with normalized positions. Shares the extraction
/// <see cref="ChatClient"/> (nornis-extract, gpt-5.4 — multimodal).
/// </summary>
public class AzureOpenAiMapExtractionClient : IMapExtractionClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiMapExtractionClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AzureOpenAiMapExtractionClient(
        ChatClient chatClient, ILogger<AzureOpenAiMapExtractionClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MapExtractionResponse> ExtractAsync(MapExtractionRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(BuildUserContext(request)),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(request.ImageBytes), request.MediaType)
            };

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(BuildSystemPrompt()),
                new UserChatMessage(parts)
            };

            var completionOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "map_places",
                    jsonSchema: BinaryData.FromString(GetStructuredOutputSchema()),
                    // Unlike text extraction there is no open proposedValue object here,
                    // so the schema is strict-mode compliant.
                    jsonSchemaIsStrict: true)
            };

            var response = await _chatClient.CompleteChatAsync(messages, completionOptions, linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var content = chatCompletion.Content.Count > 0 ? chatCompletion.Content[0].Text : string.Empty;
            var places = ParsePlaces(content);
            var usage = chatCompletion.Usage;

            return new MapExtractionResponse
            {
                Places = places,
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
            _logger.LogWarning("Map extraction timed out after {TimeoutSeconds}s", request.TimeoutSeconds);
            throw new AiExtractionTimeoutException(
                $"Map extraction timed out after {request.TimeoutSeconds} seconds.",
                (int)stopwatch.ElapsedMilliseconds);
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Map extraction failed with status {Status}", ex.Status);
            throw new HttpRequestException($"Map extraction failed: HTTP {ex.Status}", ex, (HttpStatusCode)ex.Status);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
    }

    private static string BuildUserContext(MapExtractionRequest request)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.SourceTitle))
        {
            parts.Add($"Map title: {request.SourceTitle}");
        }

        if (!string.IsNullOrWhiteSpace(request.SourceBody))
        {
            parts.Add($"Notes from the uploader (naming context only):\n{request.SourceBody}");
        }

        if (request.ExistingLocations.Count > 0)
        {
            parts.Add("Known Locations in this world (match against these before inventing new ones):");
            parts.AddRange(request.ExistingLocations.Select(l => $"- {l.Name} (Id: {l.Id})"));
        }
        else
        {
            parts.Add("This world has no known Locations yet.");
        }

        parts.Add("Extract the labeled places from the map image that follows.");
        return string.Join("\n", parts);
    }

    private IReadOnlyList<MapPlace> ParsePlaces(string content)
    {
        MapPlacesDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MapPlacesDocument>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse map extraction structured output");
            throw new AiExtractionParseException("Map extraction returned malformed JSON.", ex);
        }

        if (document?.Places is null)
        {
            throw new AiExtractionParseException("Map extraction response is missing the places array.");
        }

        var places = new List<MapPlace>(document.Places.Count);
        foreach (var raw in document.Places)
        {
            Guid? existingId = null;
            if (!string.IsNullOrWhiteSpace(raw.ExistingArtifactId) && Guid.TryParse(raw.ExistingArtifactId, out var parsed))
            {
                existingId = parsed;
            }

            places.Add(new MapPlace(
                raw.Name ?? string.Empty, raw.Kind, raw.X, raw.Y, raw.Confidence, existingId));
        }

        return places;
    }

    private sealed record RawPlace(string? Name, string? Kind, decimal X, decimal Y, decimal? Confidence, string? ExistingArtifactId);

    private sealed record MapPlacesDocument(List<RawPlace>? Places);

    internal static string BuildSystemPrompt()
    {
        return """
            You extract place names and their positions from a fantasy/TTRPG map image for
            Nornis, a world memory system.

            Return every labeled place you can read on the map. For each:
            - name: the label as written (fix only obvious letter-spacing artifacts).
            - kind: your best classification, or null.
            - x, y: the position of the place's marker or label anchor, normalized to the
              image: x from 0 (left edge) to 1 (right edge), y from 0 (top) to 1 (bottom).
              Point at the settlement dot or icon when one exists, else the label's center.
            - confidence: 0..1 that the name is read correctly.
            - existingArtifactId: when the place clearly matches one of the Known Locations
              provided (same place, allowing spelling variants), its Id — else null.

            Do not invent places that are not labeled on the map. Skip compass roses, scale
            bars, cartouches, legends, and decorative creatures.
            """;
    }

    internal static string GetStructuredOutputSchema()
    {
        return """
            {
              "type": "object",
              "properties": {
                "places": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "name": { "type": "string" },
                      "kind": {
                        "type": ["string", "null"],
                        "enum": ["city", "town", "village", "fortress", "ruin", "landmark", "region", "body_of_water", "mountain", "forest", "road", "other", null]
                      },
                      "x": { "type": "number" },
                      "y": { "type": "number" },
                      "confidence": { "type": ["number", "null"] },
                      "existingArtifactId": { "type": ["string", "null"] }
                    },
                    "required": ["name", "kind", "x", "y", "confidence", "existingArtifactId"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["places"],
              "additionalProperties": false
            }
            """;
    }
}
