using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IContinuityFixAiClient"/>. Uses the same
/// <see cref="ChatClient"/> (and therefore the same Loremaster Azure OpenAI configuration) as
/// the audit, with a strict JSON-schema structured output describing the proposals array.
/// </summary>
public class AzureOpenAiContinuityFixClient : IContinuityFixAiClient
{

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiContinuityFixClient> _logger;

    public AzureOpenAiContinuityFixClient(ChatClient chatClient, ILogger<AzureOpenAiContinuityFixClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ContinuityFixAiResponse> DraftAsync(AiPromptRequest request, CancellationToken ct)
    {
        var completionOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "continuity_fix_proposals",
                jsonSchema: BinaryData.FromString(GetStructuredOutputSchema()),
                jsonSchemaIsStrict: true)
        };

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, completionOptions, "Continuity fix", _logger, ct);

        return new ContinuityFixAiResponse
        {
            Proposals = ParseProposals(content),
            Usage = usage
        };
    }

    internal static string GetStructuredOutputSchema()
    {
        return """
            {
              "type": "object",
              "properties": {
                "proposals": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "changeType": {
                        "type": "string",
                        "enum": ["UpdateFact", "UpdateArtifact", "UpdateRelationship", "AddFact"]
                      },
                      "targetRef": { "type": "string" },
                      "rationale": { "type": "string" },
                      "name": { "type": ["string", "null"] },
                      "summary": { "type": ["string", "null"] },
                      "status": { "type": ["string", "null"] },
                      "predicate": { "type": ["string", "null"] },
                      "value": { "type": ["string", "null"] },
                      "truthState": { "type": ["string", "null"] },
                      "relationshipType": { "type": ["string", "null"] },
                      "description": { "type": ["string", "null"] },
                      "confidence": { "type": ["number", "null"] }
                    },
                    "required": ["changeType", "targetRef", "rationale", "name", "summary", "status", "predicate", "value", "truthState", "relationshipType", "description", "confidence"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["proposals"],
              "additionalProperties": false
            }
            """;
    }

    private static IReadOnlyList<ContinuityFixProposal> ParseProposals(string content)
    {
        var document = JsonNode.Parse(content)
            ?? throw new HttpRequestException("Continuity fix AI response was null or empty.");

        if (document["proposals"] is not JsonArray proposalsArray)
        {
            throw new HttpRequestException("Continuity fix AI response missing 'proposals' array.");
        }

        var proposals = new List<ContinuityFixProposal>(proposalsArray.Count);

        foreach (var node in proposalsArray)
        {
            if (node is null)
                continue;

            proposals.Add(new ContinuityFixProposal
            {
                ChangeType = node["changeType"]?.GetValue<string>() ?? string.Empty,
                TargetRef = node["targetRef"]?.GetValue<string>() ?? string.Empty,
                Rationale = node["rationale"]?.GetValue<string>() ?? string.Empty,
                Name = node["name"]?.GetValue<string>(),
                Summary = node["summary"]?.GetValue<string>(),
                Status = node["status"]?.GetValue<string>(),
                Predicate = node["predicate"]?.GetValue<string>(),
                Value = node["value"]?.GetValue<string>(),
                TruthState = node["truthState"]?.GetValue<string>(),
                RelationshipType = node["relationshipType"]?.GetValue<string>(),
                Description = node["description"]?.GetValue<string>(),
                Confidence = node["confidence"]?.GetValue<decimal>()
            });
        }

        return proposals;
    }
}
