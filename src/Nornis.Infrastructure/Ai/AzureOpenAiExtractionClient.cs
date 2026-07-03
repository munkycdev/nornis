using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiExtractionClient : IAiExtractionClient
{
    private readonly ChatClient _chatClient;
    private readonly ExtractionOptions _options;
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
        IOptions<ExtractionOptions> options,
        ILogger<AzureOpenAiExtractionClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiExtractionResponse> ExtractAsync(ExtractionRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AiTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var messages = BuildMessages(request);
            var completionOptions = BuildCompletionOptions();

            var response = await _chatClient.CompleteChatAsync(
                messages,
                completionOptions,
                linkedCts.Token);

            stopwatch.Stop();

            var chatCompletion = response.Value;
            var content = chatCompletion.Content[0].Text;

            var proposals = ParseAndValidateResponse(content);
            var usage = chatCompletion.Usage;

            return new AiExtractionResponse
            {
                Proposals = proposals,
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Model = _options.AiModel
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("AI extraction call timed out after {TimeoutSeconds}s", _options.AiTimeoutSeconds);
            throw new AiExtractionTimeoutException(
                $"AI extraction timed out after {_options.AiTimeoutSeconds} seconds.",
                (int)stopwatch.ElapsedMilliseconds);
        }
        catch (ClientResultException ex) when (IsTransientStatusCode(ex))
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Transient AI service error (status {Status})", ex.Status);
            throw new HttpRequestException(
                $"Transient AI service error: HTTP {ex.Status}",
                ex,
                (HttpStatusCode)ex.Status);
        }
        catch (ClientResultException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "AI call failed with status {Status}", ex.Status);
            throw new HttpRequestException(
                $"AI call failed: HTTP {ex.Status}",
                ex,
                (HttpStatusCode)ex.Status);
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to parse AI structured output response");
            throw new AiExtractionParseException("Failed to parse AI structured output response.", ex);
        }
        catch (AiExtractionParseException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (AiExtractionTimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error during AI extraction");
            throw new HttpRequestException("Unexpected error during AI extraction.", ex);
        }
    }

    private List<ChatMessage> BuildMessages(ExtractionRequest request)
    {
        var systemPrompt = BuildSystemPrompt(request);
        var userMessage = BuildUserMessage(request);

        return
        [
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        ];
    }

    internal static string BuildSystemPrompt(ExtractionRequest request)
    {
        return $"""
            You are an AI extraction assistant for a tabletop RPG campaign knowledge management system called Nornis.
            Your task is to analyze source material and extract structured proposals for campaign knowledge updates.

            ## Visibility Rules
            The source has visibility scope: {request.SourceVisibility}.
            All proposals you generate MUST have visibility set to "{request.SourceVisibility}".
            - Private sources produce ONLY Private proposals.
            - GMOnly sources produce ONLY GMOnly proposals.
            - PartyVisible sources produce ONLY PartyVisible proposals.
            Never produce a proposal with visibility broader than the source.

            ## Truth State Defaults
            Apply conservative truth state defaults based on context:
            - Direct observations in session notes or journal entries: assign TruthState "Likely" or "Confirmed".
            - Character claims or dialogue: assign TruthState "Rumor" or "Disputed".
            - GM notes: assign TruthState "Hidden" or "Confirmed" depending on phrasing.
            - Player theories or speculation: assign TruthState "Rumor".

            ## Rationale
            Provide a clear, concise rationale for each proposal explaining why the change is warranted based on the source content.

            ## Output Format
            Respond with a JSON object matching the structured output schema. The "proposals" array should contain 0 to 50 proposal objects.
            Each proposal must have:
            - changeType: one of "CreateArtifact", "UpdateArtifact", "MergeArtifact", "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship"
            - targetType: one of "Artifact", "ArtifactFact", "ArtifactRelationship"
            - targetId: a UUID string if updating an existing entity, or null for new entities
            - proposedValue: an object with the proposed data (include a "visibility" field set to "{request.SourceVisibility}")
            - rationale: a string explaining the proposal (1-500 characters)
            - confidence: a number between 0.0 and 1.0

            If nothing in the source warrants a proposal, return an empty proposals array.
            """;
    }

    internal static string BuildUserMessage(ExtractionRequest request)
    {
        var parts = new List<string>
        {
            $"## Source Information",
            $"- Title: {request.SourceTitle}",
            $"- Type: {request.SourceType}",
            $"- Visibility: {request.SourceVisibility}"
        };

        if (request.OccurredAt.HasValue)
        {
            parts.Add($"- Occurred At: {request.OccurredAt.Value:O}");
        }

        parts.Add("");
        parts.Add("## Source Content");
        parts.Add(request.SourceBody);

        if (request.ExistingArtifacts.Count > 0)
        {
            parts.Add("");
            parts.Add("## Existing Campaign Artifacts");
            parts.Add("Use these to avoid creating duplicates. Reference their IDs when proposing updates.");
            parts.Add("");

            foreach (var artifact in request.ExistingArtifacts)
            {
                parts.Add($"### {artifact.Name} (Id: {artifact.Id}, Type: {artifact.Type})");

                if (!string.IsNullOrWhiteSpace(artifact.Summary))
                {
                    parts.Add($"Summary: {artifact.Summary}");
                }

                if (artifact.Facts.Count > 0)
                {
                    parts.Add("Known facts:");
                    foreach (var fact in artifact.Facts)
                    {
                        parts.Add($"  - {fact.Predicate}: {fact.Value}");
                    }
                }

                parts.Add("");
            }
        }

        return string.Join("\n", parts);
    }

    private ChatCompletionOptions BuildCompletionOptions()
    {
        var schema = BinaryData.FromString(GetStructuredOutputSchema());

        return new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "extraction_proposals",
                jsonSchema: schema,
                jsonSchemaIsStrict: true)
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
            ?? throw new AiExtractionParseException("AI response was null or empty.");

        var proposalsNode = document["proposals"]
            ?? throw new AiExtractionParseException("AI response missing required 'proposals' field.");

        if (proposalsNode is not JsonArray proposalsArray)
        {
            throw new AiExtractionParseException("AI response 'proposals' field is not an array.");
        }

        if (proposalsArray.Count > 50)
        {
            throw new AiExtractionParseException(
                $"AI response contains {proposalsArray.Count} proposals, exceeding maximum of 50.");
        }

        if (proposalsArray.Count == 0)
        {
            return [];
        }

        var proposals = new List<ExtractionProposal>(proposalsArray.Count);

        for (var i = 0; i < proposalsArray.Count; i++)
        {
            var proposalNode = proposalsArray[i]
                ?? throw new AiExtractionParseException($"Proposal at index {i} is null.");

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
            throw new AiExtractionParseException(
                $"Proposal at index {index} has invalid changeType '{changeType}'.");
        }

        if (!ValidTargetTypes.Contains(targetType))
        {
            throw new AiExtractionParseException(
                $"Proposal at index {index} has invalid targetType '{targetType}'.");
        }

        if (rationale.Length == 0)
        {
            throw new AiExtractionParseException(
                $"Proposal at index {index} has empty rationale.");
        }

        if (rationale.Length > 500)
        {
            throw new AiExtractionParseException(
                $"Proposal at index {index} has rationale exceeding 500 characters ({rationale.Length}).");
        }

        var confidenceNode = node["confidence"]
            ?? throw new AiExtractionParseException($"Proposal at index {index} missing required 'confidence' field.");

        var confidence = confidenceNode.GetValue<decimal>();
        if (confidence < 0.0m || confidence > 1.0m)
        {
            throw new AiExtractionParseException(
                $"Proposal at index {index} has confidence {confidence} outside valid range 0.0-1.0.");
        }

        var proposedValueNode = node["proposedValue"]
            ?? throw new AiExtractionParseException($"Proposal at index {index} missing required 'proposedValue' field.");

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
                throw new AiExtractionParseException(
                    $"Proposal at index {index} has invalid targetId '{targetIdStr}' (expected UUID or null).");
            }
        }

        // Deserialize proposedValue as a dynamic object for flexibility
        var proposedValue = JsonSerializer.Deserialize<object>(proposedValueNode.ToJsonString(), JsonOptions)
            ?? throw new AiExtractionParseException($"Proposal at index {index} has null proposedValue after deserialization.");

        return new ExtractionProposal
        {
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValue = proposedValue,
            Rationale = rationale,
            Confidence = confidence
        };
    }

    private static string GetRequiredString(JsonNode node, string fieldName, int proposalIndex)
    {
        var fieldNode = node[fieldName]
            ?? throw new AiExtractionParseException(
                $"Proposal at index {proposalIndex} missing required '{fieldName}' field.");

        if (fieldNode.GetValueKind() != JsonValueKind.String)
        {
            throw new AiExtractionParseException(
                $"Proposal at index {proposalIndex} has non-string '{fieldName}' field.");
        }

        return fieldNode.GetValue<string>();
    }

    private static bool IsTransientStatusCode(ClientResultException ex)
    {
        return ex.Status == (int)HttpStatusCode.TooManyRequests ||
               ex.Status == (int)HttpStatusCode.ServiceUnavailable;
    }
}
