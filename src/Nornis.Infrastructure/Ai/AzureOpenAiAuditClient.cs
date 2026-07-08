using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IAuditAiClient"/>. Uses the same <see cref="ChatClient"/>
/// (and therefore the same Loremaster Azure OpenAI configuration) as the Loremaster, with a strict
/// JSON-schema structured output describing the findings array.
/// </summary>
public class AzureOpenAiAuditClient : IAuditAiClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiAuditClient> _logger;

    public AzureOpenAiAuditClient(ChatClient chatClient, ILogger<AzureOpenAiAuditClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuditAiResponse> AssessAsync(AuditAiRequest request, CancellationToken ct)
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
                    jsonSchemaFormatName: "continuity_findings",
                    jsonSchema: BinaryData.FromString(GetStructuredOutputSchema()),
                    jsonSchemaIsStrict: true)
            };

            var response = await _chatClient.CompleteChatAsync(messages, completionOptions, linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var content = chatCompletion.Content[0].Text;
            var findings = ParseFindings(content);
            var usage = chatCompletion.Usage;

            return new AuditAiResponse
            {
                Findings = findings,
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
            _logger.LogWarning("Continuity audit AI call timed out after {TimeoutSeconds}s", request.TimeoutSeconds);
            throw new TimeoutException($"Continuity audit AI call timed out after {request.TimeoutSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Continuity audit AI call failed with status {Status}", ex.Status);
            throw new HttpRequestException($"Continuity audit AI call failed: HTTP {ex.Status}", ex, (HttpStatusCode)ex.Status);
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
            _logger.LogError(ex, "Unexpected error during continuity audit AI call");
            throw new HttpRequestException("Unexpected error during continuity audit AI call.", ex);
        }
    }

    internal static string GetStructuredOutputSchema()
    {
        return """
            {
              "type": "object",
              "properties": {
                "findings": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "category": {
                        "type": "string",
                        "enum": ["Contradiction", "DanglingThread", "StaleStoryline", "TimelineConflict", "SummaryDrift"]
                      },
                      "severity": {
                        "type": "string",
                        "enum": ["High", "Medium", "Low"]
                      },
                      "summary": {
                        "type": "string"
                      },
                      "suggestedAction": {
                        "type": ["string", "null"]
                      },
                      "evidence": {
                        "type": "array",
                        "items": { "type": "string" }
                      },
                      "artifactRef": {
                        "type": ["string", "null"]
                      }
                    },
                    "required": ["category", "severity", "summary", "suggestedAction", "evidence", "artifactRef"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["findings"],
              "additionalProperties": false
            }
            """;
    }

    private static IReadOnlyList<AuditFinding> ParseFindings(string content)
    {
        var document = JsonNode.Parse(content)
            ?? throw new HttpRequestException("Continuity audit AI response was null or empty.");

        if (document["findings"] is not JsonArray findingsArray)
        {
            throw new HttpRequestException("Continuity audit AI response missing 'findings' array.");
        }

        var findings = new List<AuditFinding>(findingsArray.Count);

        foreach (var node in findingsArray)
        {
            if (node is null)
                continue;

            var evidence = new List<string>();
            if (node["evidence"] is JsonArray evidenceArray)
            {
                foreach (var e in evidenceArray)
                {
                    var value = e?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                        evidence.Add(value);
                }
            }

            findings.Add(new AuditFinding
            {
                Category = node["category"]?.GetValue<string>() ?? string.Empty,
                Severity = node["severity"]?.GetValue<string>() ?? string.Empty,
                Summary = node["summary"]?.GetValue<string>() ?? string.Empty,
                SuggestedAction = node["suggestedAction"]?.GetValue<string>(),
                Evidence = evidence,
                ArtifactRef = node["artifactRef"]?.GetValue<string>()
            });
        }

        return findings;
    }
}
