using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IRelationshipBackfillAiClient"/>. Shares the
/// extraction <see cref="ChatClient"/> (nornis-extract deployment), with strict
/// JSON-schema structured output describing the links array.
/// </summary>
public class AzureOpenAiRelationshipBackfillClient : IRelationshipBackfillAiClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiRelationshipBackfillClient> _logger;

    public AzureOpenAiRelationshipBackfillClient(ChatClient chatClient, ILogger<AzureOpenAiRelationshipBackfillClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RelationshipBackfillAiResponse> ProposeLinksAsync(RelationshipBackfillAiRequest request, CancellationToken ct)
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

            var completionOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "backfill_links",
                    jsonSchema: BinaryData.FromString(GetStructuredOutputSchema()),
                    jsonSchemaIsStrict: true)
            };

            var response = await _chatClient.CompleteChatAsync(messages, completionOptions, linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var content = chatCompletion.Content[0].Text;
            var links = ParseLinks(content);
            var usage = chatCompletion.Usage;

            return new RelationshipBackfillAiResponse
            {
                Links = links,
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
            _logger.LogWarning("Relationship backfill AI call timed out after {TimeoutSeconds}s", request.TimeoutSeconds);
            throw new TimeoutException($"Relationship backfill AI call timed out after {request.TimeoutSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Relationship backfill AI call failed with status {Status}", ex.Status);
            throw new HttpRequestException($"Relationship backfill AI call failed: HTTP {ex.Status}", ex, (HttpStatusCode)ex.Status);
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
            _logger.LogError(ex, "Unexpected error during relationship backfill AI call");
            throw new HttpRequestException("Unexpected error during relationship backfill AI call.", ex);
        }
    }

    internal static string GetStructuredOutputSchema()
    {
        return """
            {
              "type": "object",
              "properties": {
                "links": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "artifactAName": {
                        "type": "string"
                      },
                      "artifactBName": {
                        "type": "string"
                      },
                      "type": {
                        "type": "string",
                        "enum": ["Advances", "PartOf"]
                      },
                      "description": {
                        "type": ["string", "null"]
                      },
                      "rationale": {
                        "type": "string"
                      },
                      "quote": {
                        "type": ["string", "null"]
                      },
                      "confidence": {
                        "type": "number"
                      }
                    },
                    "required": ["artifactAName", "artifactBName", "type", "description", "rationale", "quote", "confidence"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["links"],
              "additionalProperties": false
            }
            """;
    }

    private static IReadOnlyList<BackfillLinkProposal> ParseLinks(string content)
    {
        var document = JsonNode.Parse(content)
            ?? throw new HttpRequestException("Relationship backfill AI response was null or empty.");

        if (document["links"] is not JsonArray linksArray)
        {
            throw new HttpRequestException("Relationship backfill AI response missing 'links' array.");
        }

        var links = new List<BackfillLinkProposal>(linksArray.Count);

        foreach (var node in linksArray)
        {
            if (node is null)
                continue;

            links.Add(new BackfillLinkProposal
            {
                ArtifactAName = node["artifactAName"]?.GetValue<string>() ?? string.Empty,
                ArtifactBName = node["artifactBName"]?.GetValue<string>() ?? string.Empty,
                Type = node["type"]?.GetValue<string>() ?? string.Empty,
                Description = node["description"]?.GetValue<string>(),
                Rationale = node["rationale"]?.GetValue<string>() ?? string.Empty,
                Quote = node["quote"]?.GetValue<string>(),
                Confidence = node["confidence"]?.GetValue<decimal>()
            });
        }

        return links;
    }
}
