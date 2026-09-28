using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IAuditAiClient"/>. Uses the same <see cref="IChatClient"/>
/// (and therefore the same Loremaster Azure OpenAI configuration) as the Loremaster, with a strict
/// JSON-schema structured output describing the findings array.
/// </summary>
public class AzureOpenAiAuditClient : IAuditAiClient
{

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiAuditClient> _logger;

    public AzureOpenAiAuditClient(IChatClient chatClient, ILogger<AzureOpenAiAuditClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<AuditAiResponse> AssessAsync(AiPromptRequest request, CancellationToken ct)
    {
        var chatOptions = AzureOpenAiCallExecutor.JsonSchema("continuity_findings", GetStructuredOutputSchema(), strict: true);

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, chatOptions, "Continuity audit", _logger, ct);

        return new AuditAiResponse
        {
            Findings = ParseFindings(content),
            Usage = usage
        };
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
                        "enum": ["Contradiction", "DanglingThread", "StaleStoryline", "TimelineConflict", "SummaryDrift", "DuplicateArtifact"]
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
            ?? throw new AiParseException("Continuity audit AI response was null or empty.");

        if (document["findings"] is not JsonArray findingsArray)
        {
            throw new AiParseException("Continuity audit AI response missing 'findings' array.");
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
