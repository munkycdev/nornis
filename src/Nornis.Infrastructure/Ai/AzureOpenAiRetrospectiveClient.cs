using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IRetrospectiveAiClient"/>. Shares the
/// Loremaster <see cref="IChatClient"/> configuration, with strict JSON-schema
/// structured output describing the verdicts array.
/// </summary>
public class AzureOpenAiRetrospectiveClient : IRetrospectiveAiClient
{

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiRetrospectiveClient> _logger;

    public AzureOpenAiRetrospectiveClient(IChatClient chatClient, ILogger<AzureOpenAiRetrospectiveClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<RetrospectiveAiResponse> AssessAsync(AiPromptRequest request, CancellationToken ct)
    {
        var chatOptions = AzureOpenAiCallExecutor.JsonSchema("storyline_verdicts", GetStructuredOutputSchema(), strict: true);

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, chatOptions, "Storyline retrospective", _logger, ct);

        return new RetrospectiveAiResponse
        {
            Verdicts = ParseVerdicts(content),
            Usage = usage
        };
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
