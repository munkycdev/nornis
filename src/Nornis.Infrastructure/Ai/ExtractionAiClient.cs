using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

public class ExtractionAiClient : IAiExtractionClient
{
    // DO NOT set an output ceiling here (ChatOptions.MaxOutputTokens, or the SDK's
    // MaxOutputTokenCount) or in any sibling client. Azure.AI.OpenAI 2.1.0 serialised it as
    // "max_tokens", and the gpt-5.4 deployments reject that outright:
    //
    //     HTTP 400 (invalid_request_error: unsupported_parameter) Parameter: max_tokens
    //     "'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead."
    //
    // It fails the call before any tokens are spent, so there is no partial-response fallback:
    // the feature is simply dead. Setting it took every AI feature down in production on
    // 2026-07-27. The OpenAI SDK that replaced it may well emit max_completion_tokens; restore
    // a ceiling only after a real call against the deployment proves it.

    private readonly IAiCallBinder _binder;
    private readonly ILogger<ExtractionAiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ValidChangeTypes =
    [
        "CreateArtifact", "UpdateArtifact", "MergeArtifact",
        "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship"
    ];

    private static readonly string[] ValidTargetTypes =
    [
        "Artifact", "ArtifactFact", "ArtifactRelationship"
    ];

    /// <summary>The schema's enum, exactly; Enum.TryParse alone would also accept "1".</summary>
    private static readonly string[] SignificanceNames = Enum.GetNames<ProposalSignificance>();

    public ExtractionAiClient(
        IAiCallBinder binder,
        ILogger<ExtractionAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    public async Task<AiExtractionResponse> ExtractAsync(AiPromptRequest request, CancellationToken ct)
    {
        // A failure's response body (content-filter verdicts and the like) is logged by the
        // provider's traits, for every client, not only this one.
        var result = await AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Extraction, request, CallOptions, "AI extraction", _logger, ct);

        try
        {
            return new AiExtractionResponse
            {
                Proposals = ParseAndValidateResponse(result.Content),
                Usage = result.Usage
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI structured output response");
            // Carry the tokens this attempt was billed for; the retry loop meters them.
            throw new AiParseException("Failed to parse AI structured output response.", ex)
            {
                Usage = result.Usage
            };
        }
    }

    /// <summary>Internal so a test can assert the request we are about to send is one these
    /// deployments will accept — see the note at the top of this class.</summary>
    internal static AiCallOptions CallOptions { get; } =
        new(new AiJsonSchema("extraction_proposals", GetStructuredOutputSchema()));

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
                        "enum": ["CreateArtifact", "UpdateArtifact", "MergeArtifact", "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship"]
                      },
                      "targetType": {
                        "type": "string",
                        "enum": ["Artifact", "ArtifactFact", "ArtifactRelationship"]
                      },
                      "targetId": {
                        "type": ["string", "null"]
                      },
                      "proposedValue": {
                        "type": "object",
                        "properties": {
                            "name": { "type": ["string", "null"] },
                            "type": { "type": ["string", "null"] },
                            "summary": { "type": ["string", "null"] },
                            "visibility": { "type": ["string", "null"] },
                            "confidence": { "type": ["number", "null"] },
                            "status": { "type": ["string", "null"] },
                            "sourceArtifactId": { "type": ["string", "null"] },
                            "predicate": { "type": ["string", "null"] },
                            "value": { "type": ["string", "null"] },
                            "truthState": { "type": ["string", "null"] },
                            "artifactName": { "type": ["string", "null"] },
                            "artifactAId": { "type": ["string", "null"] },
                            "artifactBId": { "type": ["string", "null"] },
                            "artifactAName": { "type": ["string", "null"] },
                            "artifactBName": { "type": ["string", "null"] },
                            "description": { "type": ["string", "null"] }
                        },
                        "required": ["name", "type", "summary", "visibility", "confidence", "status", "sourceArtifactId", "predicate", "value", "truthState", "artifactName", "artifactAId", "artifactBId", "artifactAName", "artifactBName", "description"],
                        "additionalProperties": false
                      },
                      "rationale": {
                        "type": "string"
                      },
                      "confidence": {
                        "type": "number"
                      },
                      "significance": {
                        "type": "string",
                        "enum": ["Core", "Supporting", "Incidental"]
                      },
                      "quote": {
                        "type": ["string", "null"]
                      }
                    },
                    "required": ["changeType", "targetType", "targetId", "proposedValue", "rationale", "confidence", "significance", "quote"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["proposals"],
              "additionalProperties": false
            }
            """;
    }

    private IReadOnlyList<ExtractionProposal> ParseAndValidateResponse(string content)
    {
        var document = JsonNode.Parse(content)
            ?? throw new AiParseException("AI response was null or empty.");

        var proposalsNode = document["proposals"]
            ?? throw new AiParseException("AI response missing required 'proposals' field.");

        if (proposalsNode is not JsonArray proposalsArray)
        {
            throw new AiParseException("AI response 'proposals' field is not an array.");
        }

        // Rich sources can legitimately overrun the 50-proposal cap the prompt asks for.
        // Keep the first 50 (the prompt orders CreateArtifact proposals first, so creates
        // survive) rather than failing the whole extraction and losing everything.
        if (proposalsArray.Count > 50)
        {
            _logger.LogWarning(
                "AI response contains {ProposalCount} proposals; keeping the first 50 and dropping the rest.",
                proposalsArray.Count);

            while (proposalsArray.Count > 50)
            {
                proposalsArray.RemoveAt(proposalsArray.Count - 1);
            }
        }

        if (proposalsArray.Count == 0)
        {
            return [];
        }

        var proposals = new List<ExtractionProposal>(proposalsArray.Count);

        for (var i = 0; i < proposalsArray.Count; i++)
        {
            var proposalNode = proposalsArray[i]
                ?? throw new AiParseException($"Proposal at index {i} is null.");

            proposals.Add(ParseProposal(proposalNode, i));
        }

        return proposals;
    }

    private static ExtractionProposal ParseProposal(JsonNode node, int index)
    {
        var changeType = GetRequiredString(node, "changeType", index);
        var targetType = GetRequiredString(node, "targetType", index);
        var rationale = GetRequiredString(node, "rationale", index);

        if (!ValidChangeTypes.Contains(changeType))
        {
            throw new AiParseException(
                $"Proposal at index {index} has invalid changeType '{changeType}'.");
        }

        if (!ValidTargetTypes.Contains(targetType))
        {
            throw new AiParseException(
                $"Proposal at index {index} has invalid targetType '{targetType}'.");
        }

        if (rationale.Length == 0)
        {
            throw new AiParseException(
                $"Proposal at index {index} has empty rationale.");
        }

        if (rationale.Length > 500)
        {
            throw new AiParseException(
                $"Proposal at index {index} has rationale exceeding 500 characters ({rationale.Length}).");
        }

        var confidenceNode = node["confidence"]
            ?? throw new AiParseException($"Proposal at index {index} missing required 'confidence' field.");

        var confidence = confidenceNode.GetValue<decimal>();
        if (confidence < 0.0m || confidence > 1.0m)
        {
            throw new AiParseException(
                $"Proposal at index {index} has confidence {confidence} outside valid range 0.0-1.0.");
        }

        var proposedValueNode = node["proposedValue"]
            ?? throw new AiParseException($"Proposal at index {index} missing required 'proposedValue' field.");

        // Absent or null is Supporting: a model that did not say has not called the proposal
        // incidental (feature 43). A value outside the enum is a bad answer like a bad changeType.
        var significance = ProposalSignificance.Supporting;
        var significanceNode = node["significance"];
        if (significanceNode is not null && significanceNode.GetValueKind() != JsonValueKind.Null)
        {
            var raw = significanceNode.GetValueKind() == JsonValueKind.String ? significanceNode.GetValue<string>() : significanceNode.ToJsonString();
            if (!SignificanceNames.Contains(raw))
            {
                throw new AiParseException(
                    $"Proposal at index {index} has invalid significance '{raw}' (expected Core, Supporting or Incidental).");
            }

            significance = Enum.Parse<ProposalSignificance>(raw);
        }

        string? quote = null;
        var quoteNode = node["quote"];
        if (quoteNode is not null && quoteNode.GetValueKind() == JsonValueKind.String)
        {
            quote = quoteNode.GetValue<string>().Trim();
            if (quote.Length == 0)
            {
                quote = null;
            }
            else if (quote.Length > 500)
            {
                quote = quote[..500];
            }
        }

        Guid? targetId = null;
        var targetIdNode = node["targetId"];
        if (targetIdNode is not null && targetIdNode.GetValueKind() != JsonValueKind.Null)
        {
            var targetIdStr = targetIdNode.GetValue<string>();
            if (Guid.TryParse(targetIdStr, out var parsedGuid))
            {
                targetId = parsedGuid;
            }
            else
            {
                throw new AiParseException(
                    $"Proposal at index {index} has invalid targetId '{targetIdStr}' (expected UUID or null).");
            }
        }

        // The strict schema makes the model send every payload field, with null for the ones its
        // changeType doesn't use. Dropping those nulls gives back exactly the payload the open
        // schema used to produce, so nothing downstream sees the difference (feature 39).
        var proposedValue = JsonSerializer.Deserialize<object>(WithoutNulls(proposedValueNode).ToJsonString(), JsonOptions)
            ?? throw new AiParseException($"Proposal at index {index} has null proposedValue after deserialization.");

        return new ExtractionProposal
        {
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValue = proposedValue,
            Rationale = rationale,
            Confidence = confidence,
            Quote = quote,
            Significance = significance
        };
    }

    /// <summary>The payload without its null fields; anything that isn't an object passes through.</summary>
    internal static JsonNode WithoutNulls(JsonNode proposedValue)
    {
        if (proposedValue is not JsonObject payload)
        {
            return proposedValue;
        }

        var kept = new JsonObject();
        foreach (var (name, value) in payload)
        {
            if (value is not null && value.GetValueKind() != JsonValueKind.Null)
            {
                kept[name] = value.DeepClone();
            }
        }

        return kept;
    }

    private static string GetRequiredString(JsonNode node, string fieldName, int proposalIndex)
    {
        var fieldNode = node[fieldName]
            ?? throw new AiParseException(
                $"Proposal at index {proposalIndex} missing required '{fieldName}' field.");

        if (fieldNode.GetValueKind() != JsonValueKind.String)
        {
            throw new AiParseException(
                $"Proposal at index {proposalIndex} has non-string '{fieldName}' field.");
        }

        return fieldNode.GetValue<string>();
    }
}
