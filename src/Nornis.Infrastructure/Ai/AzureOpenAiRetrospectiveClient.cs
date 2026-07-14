using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IRetrospectiveAiClient"/>. Shares the
/// Loremaster <see cref="ChatClient"/> configuration, with strict JSON-schema
/// structured output describing the verdicts array.
/// </summary>
public class AzureOpenAiRetrospectiveClient : IRetrospectiveAiClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiRetrospectiveClient> _logger;

    public AzureOpenAiRetrospectiveClient(ChatClient chatClient, ILogger<AzureOpenAiRetrospectiveClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RetrospectiveAiResponse> AssessAsync(RetrospectiveAiRequest request, CancellationToken ct)
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
                    jsonSchemaFormatName: "storyline_verdicts",
                    jsonSchema: BinaryData.FromString(GetStructuredOutputSchema()),
                    jsonSchemaIsStrict: true)
            };

            var response = await _chatClient.CompleteChatAsync(messages, completionOptions, linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var content = chatCompletion.Content[0].Text;
            var verdicts = ParseVerdicts(content);
            var usage = chatCompletion.Usage;

            return new RetrospectiveAiResponse
            {
                Verdicts = verdicts,
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
            _logger.LogWarning("Storyline retrospective AI call timed out after {TimeoutSeconds}s", request.TimeoutSeconds);
            throw new TimeoutException($"Storyline retrospective AI call timed out after {request.TimeoutSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Storyline retrospective AI call failed with status {Status}", ex.Status);
            throw new HttpRequestException($"Storyline retrospective AI call failed: HTTP {ex.Status}", ex, (HttpStatusCode)ex.Status);
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
            _logger.LogError(ex, "Unexpected error during storyline retrospective AI call");
            throw new HttpRequestException("Unexpected error during storyline retrospective AI call.", ex);
        }
    }

    internal static string GetStructuredOutputSchema()
    {
        return """
            {
              "type": "object",
              "properties": {
                "verdicts": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "storylineId": {
                        "type": "string"
                      },
                      "verdict": {
                        "type": "string",
                        "enum": ["Resolved", "Dormant", "StillActive"]
                      },
                      "rationale": {
                        "type": "string"
                      },
                      "confidence": {
                        "type": "number"
                      }
                    },
                    "required": ["storylineId", "verdict", "rationale", "confidence"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["verdicts"],
              "additionalProperties": false
            }
            """;
    }

    private static IReadOnlyList<RetrospectiveVerdict> ParseVerdicts(string content)
    {
        var document = JsonNode.Parse(content)
            ?? throw new HttpRequestException("Storyline retrospective AI response was null or empty.");

        if (document["verdicts"] is not JsonArray verdictsArray)
        {
            throw new HttpRequestException("Storyline retrospective AI response missing 'verdicts' array.");
        }

        var verdicts = new List<RetrospectiveVerdict>(verdictsArray.Count);

        foreach (var node in verdictsArray)
        {
            if (node is null)
                continue;

            verdicts.Add(new RetrospectiveVerdict
            {
                StorylineId = node["storylineId"]?.GetValue<string>() ?? string.Empty,
                Verdict = node["verdict"]?.GetValue<string>() ?? string.Empty,
                Rationale = node["rationale"]?.GetValue<string>() ?? string.Empty,
                Confidence = node["confidence"]?.GetValue<decimal>()
            });
        }

        return verdicts;
    }
}
