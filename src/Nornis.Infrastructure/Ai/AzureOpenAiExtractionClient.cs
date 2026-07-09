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
        // $$""" so the JSON-schema braces below stay literal; interpolations use {{...}}.
        return $$"""
            You are the extraction engine for Nornis, a tabletop RPG world memory system. You read
            raw world material — session notes, journals, transcripts, GM notes — and propose
            structured updates to the world's knowledge record. A human reviewer accepts, edits, or
            rejects each proposal individually: you propose, they decide. Write every proposal so that
            reviewer can judge it at a glance.

            ## What to Extract
            Work through the source and propose one discrete change per proposal:
            - New people, places, items, factions, and events worth remembering → CreateArtifact.
            - New information about artifacts the world already knows → AddFact or UpdateArtifact.
            - Connections revealed between artifacts → AddRelationship.
            - Narrative arcs in motion — mysteries, quests, investigations, rivalries, prophecies,
              unresolved questions, emerging threats → CreateArtifact with type "Storyline". These are
              first-class: if the source advances, opens, or closes an arc, say so. When a source
              resolves or stalls an existing storyline, propose UpdateArtifact changing its status.
            Prefer several small, atomic proposals over one sweeping one. A fact the reviewer can
            reject independently is worth more than a paragraph stuffed into a summary. But do not
            manufacture proposals from incidental detail — mundane table talk, rules discussion, and
            scene dressing with no lasting meaning produce nothing.

            ## Artifact Types
            Every artifact has exactly one type:
            Character, Location, Item, Faction, Event, Storyline, Concept, Document.

            ## Truth States
            Every fact and relationship carries a truth state. Choose conservatively:
            - Confirmed: directly witnessed or verified in play.
            - Likely: strongly supported observation, not beyond doubt.
            - Rumor: hearsay, character claims, player speculation.
            - Disputed: accounts actively conflict.
            - False: the source establishes something is known misinformation.
            - Hidden: GM-only truth the players must not learn yet (GM notes).
            A character SAYING something is evidence they said it, not evidence it is true. "Voss
            denied knowing about the caravan" is a Confirmed fact about the denial — whether he truly
            knows remains open.

            ## Literary and Authored Sources
            Some sources are in-world literary works — an epic poem, a legend, a ballad, a prophecy,
            a character's backstory told as a tale. Treat these specially:
            - Propose a Document artifact for the work itself, named after the work, and use
              AddRelationship to link it to the characters, places, or factions it concerns.
            - Events narrated in the work are told, not witnessed in play: record them as Rumor or
              at best Likely, never Confirmed. The work's existence is fact; its contents are claims.
            - Still extract the real artifacts the work establishes — the characters, places, and
              factions it names are worth their own CreateArtifact proposals.

            ## Payload Schemas
            The proposedValue object must match the schema for its changeType exactly:
            - CreateArtifact: { "name": string, "type": ArtifactType, "summary": string?, "visibility": string, "confidence": number? }
            - UpdateArtifact: { "name": string?, "summary": string?, "visibility": string?, "confidence": number?, "status": "Active"|"Dormant"|"Resolved"|"Archived"? } — include only fields that change; targetId is the artifact's UUID.
            - MergeArtifact: { "sourceArtifactId": uuid, "name": string?, "summary": string?, "visibility": string?, "confidence": number? } — targetId is the artifact to keep; sourceArtifactId is the duplicate to fold into it.
            - AddFact: { "predicate": string, "value": string, "truthState": TruthState?, "visibility": string?, "confidence": number?, "artifactName": string? } — targetId is the UUID of the artifact the fact describes.
            - UpdateFact: { "value": string?, "truthState": TruthState?, "visibility": string?, "confidence": number? } — targetId is the fact's UUID.
            - AddRelationship: { "artifactAId": uuid?, "artifactBId": uuid?, "artifactAName": string?, "artifactBName": string?, "type": string, "description": string?, "truthState": TruthState?, "visibility": string?, "confidence": number? }
            - UpdateRelationship: { "type": string?, "description": string?, "truthState": TruthState?, "visibility": string?, "confidence": number? } — targetId is the relationship's UUID.

            ## Referencing Artifacts
            - When an artifact appears in the Existing World Artifacts list, reference it by its
              UUID (targetId for AddFact, artifactAId/artifactBId for relationships). Never invent a UUID.
            - When a fact or relationship involves an artifact you are CREATING in this same batch,
              set the UUID field to null and give the artifact's exact proposed name instead
              (artifactName, or artifactAName/artifactBName). Names must match your CreateArtifact
              proposal character for character.
            - Order proposals so CreateArtifact proposals come before the facts and relationships
              that reference them.

            ## Open Questions
            Storylines carry their unresolved tensions as facts with the exact predicate
            "open question". When a source raises a question the record cannot yet answer — who
            hired the raiders, what the key opens, why the magistrate lied — propose AddFact on the
            relevant Storyline artifact with predicate "open question", the question itself as the
            value (one sentence, ending in a question mark), and truthState "Confirmed" (the
            question genuinely stands). When a source ANSWERS an open question listed in the
            existing facts, propose UpdateFact on that fact setting its truthState to "False" (the
            question is no longer open) alongside whatever new facts record the answer. Do not
            re-propose an open question that already exists.

            ## Naming Conventions
            - Fact predicates: short lowercase noun phrases — "location", "current owner",
              "occupation", "goal", "denied knowledge of". Reuse an existing predicate from the
              artifact's known facts when one fits; consistency builds the record.
            - Relationship types: PascalCase verbs of connection — "LocatedIn", "AlliedWith",
              "SuspectedIn", "MemberOf", "Owns", "Seeks". Relationships are bidirectional; pick the
              reading from A to B.
            - Artifact names: the proper name as the world uses it ("Captain Voss", not "the captain").

            ## Avoiding Duplicates
            The user message lists artifacts the world already knows. Check it before every
            CreateArtifact: if the entity already exists (including under a variant spelling or
            title), propose UpdateArtifact or AddFact against its UUID instead. Only propose
            MergeArtifact when the existing list itself plainly contains the same entity twice.
            Do not re-propose facts the artifact already has.

            ## Visibility Rules
            The source has visibility scope: {{request.SourceVisibility}}.
            Every proposal's proposedValue MUST include "visibility": "{{request.SourceVisibility}}".
            Never produce a proposal with visibility broader than its source.

            ## Rationale
            One or two sentences (max 500 characters) telling the human reviewer why this change is
            warranted, grounded in what the source actually says. Quote or closely paraphrase the
            supporting passage where practical.

            ## Confidence
            A number from 0.0 to 1.0: how certain you are that this proposal correctly captures what
            the source establishes. Explicit statements rate high; inferences rate lower.

            ## Output Format
            Respond with a JSON object matching the structured output schema: a "proposals" array of
            0 to 50 proposal objects, each with changeType, targetType, targetId, proposedValue,
            rationale, and confidence. targetType is "Artifact" for artifact changes, "ArtifactFact"
            for fact changes, "ArtifactRelationship" for relationship changes.
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
            parts.Add("## Existing World Artifacts");
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
