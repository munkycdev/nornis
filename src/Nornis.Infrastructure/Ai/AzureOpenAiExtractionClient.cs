using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiExtractionClient : IAiExtractionClient
{
    // DO NOT set ChatCompletionOptions.MaxOutputTokenCount here (or in any sibling client).
    // Azure.AI.OpenAI 2.1.0 — the current release — serialises it as "max_tokens", and the
    // gpt-5.4 deployments reject that outright:
    //
    //     HTTP 400 (invalid_request_error: unsupported_parameter) Parameter: max_tokens
    //     "'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead."
    //
    // It fails the call before any tokens are spent, so there is no partial-response fallback:
    // the feature is simply dead. Setting it took every AI feature down in production on
    // 2026-07-27. Restore an output ceiling only once the SDK emits max_completion_tokens, and
    // verify against a real deployment before shipping.

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiExtractionClient> _logger;

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

    public AzureOpenAiExtractionClient(
        ChatClient chatClient,
        ILogger<AzureOpenAiExtractionClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<AiExtractionResponse> ExtractAsync(AiPromptRequest request, CancellationToken ct)
    {
        var completionOptions = BuildCompletionOptions();

        (string Content, AiUsage Usage) result;
        try
        {
            result = await AzureOpenAiCallExecutor.ExecuteAsync(
                _chatClient, request, completionOptions, "AI extraction", _logger, ct);
        }
        catch (AiHttpException ex) when (ex.InnerException is ClientResultException raw)
        {
            // The response body names the exact failure — for content_filter 400s it carries
            // per-category verdicts (violence/hate/sexual/self-harm severity, jailbreak) that
            // the exception message omits. Log it or the failure is undiagnosable.
            _logger.LogError(ex, "AI call failed with status {Status}. Response body: {ResponseBody}",
                raw.Status, GetRawResponseBody(raw));
            throw;
        }

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
    internal static ChatCompletionOptions BuildCompletionOptions()
    {
        var schema = BinaryData.FromString(GetStructuredOutputSchema());

        return new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "extraction_proposals",
                jsonSchema: schema,
                // Not strict: proposedValue's shape varies by changeType, so the schema declares
                // it as an open object ("additionalProperties": true) — which strict mode rejects
                // with HTTP 400. Output shape is still guarded by ParseAndValidateResponse, the
                // parse-retry loop, and ProposalValidator at accept time.
                jsonSchemaIsStrict: false)
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
                        "additionalProperties": true
                      },
                      "rationale": {
                        "type": "string"
                      },
                      "confidence": {
                        "type": "number"
                      },
                      "quote": {
                        "type": ["string", "null"]
                      }
                    },
                    "required": ["changeType", "targetType", "proposedValue", "rationale", "confidence"],
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

        // Deserialize proposedValue as a dynamic object for flexibility
        var proposedValue = JsonSerializer.Deserialize<object>(proposedValueNode.ToJsonString(), JsonOptions)
            ?? throw new AiParseException($"Proposal at index {index} has null proposedValue after deserialization.");

        return new ExtractionProposal
        {
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValue = proposedValue,
            Rationale = rationale,
            Confidence = confidence,
            Quote = quote
        };
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

    private static string GetRawResponseBody(ClientResultException ex)
    {
        try
        {
            return ex.GetRawResponse()?.Content?.ToString() ?? "(no response body)";
        }
        catch
        {
            return "(response body unavailable)";
        }
    }
}
