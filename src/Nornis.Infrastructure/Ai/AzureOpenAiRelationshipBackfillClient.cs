using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IRelationshipBackfillAiClient"/>. Shares the
/// extraction <see cref="IChatClient"/> (nornis-extract deployment), with strict
/// JSON-schema structured output describing the links array.
/// </summary>
public class AzureOpenAiRelationshipBackfillClient : IRelationshipBackfillAiClient
{

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiRelationshipBackfillClient> _logger;

    public AzureOpenAiRelationshipBackfillClient(IChatClient chatClient, ILogger<AzureOpenAiRelationshipBackfillClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<RelationshipBackfillAiResponse> ProposeLinksAsync(AiPromptRequest request, CancellationToken ct)
    {
        var chatOptions = AzureOpenAiCallExecutor.JsonSchema("backfill_links", GetStructuredOutputSchema(), strict: true);

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, chatOptions, "Relationship backfill", _logger, ct);

        return new RelationshipBackfillAiResponse
        {
            Links = ParseLinks(content),
            Usage = usage
        };
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
