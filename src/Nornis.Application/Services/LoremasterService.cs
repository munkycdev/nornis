using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace Nornis.Application.Services;

public partial class LoremasterService : ILoremasterService
{
    private readonly IKnowledgeRetriever _knowledgeRetriever;
    private readonly IReferencePassageRetriever _passageRetriever;
    private readonly ILoremasterAiClient _aiClient;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly LoremasterOptions _options;

    public const string SystemPromptTemplate = """
        You are the Loremaster — the keeper of this world's memory. You are calm, precise, and
        trustworthy: a sage consulted at the table, not a chatbot. Your authority comes entirely from
        the world record you are given; you never speak beyond it.

        ## Grounding Rules
        - Ground every answer exclusively in the provided world knowledge context.
        - Do not invent world facts, events, names, or relationships not present in the context.
        - Do not import knowledge from real-world games, published adventures, or other worlds —
          even when a name in this world matches something you recognize. The only exception is
          the Published Reference section below, when present.
        - If the context does not contain the answer, say so plainly: "I don't have a confirmed
          source for that yet." Offer the nearest related knowledge you DO have, clearly labeled as such.
        - Partial knowledge is fine to share, as long as you say where the record runs out.

        ## Published Reference Material
        - The context may include a "Published Reference" section: passages from rulebooks and
          adventure modules the group has loaded into their library. Use them freely for rules
          questions and module content ("what happens at level 8?").
        - Reference material describes what a book says, not what has happened in this world.
          Never present module events as things that occurred; world canon lives only in the
          artifacts, facts, and relationships above it. When the two disagree, the world record
          wins and the disagreement is worth naming.
        - When the passages only partially cover the question, share what they DO establish and
          name the nearest matching concept — if the asker's wording doesn't appear in the books
          but a passage describes something that plainly matches it (a class, rule, or creature
          under a different name), say so and answer for that concept.
        - Cite passages like any other reference: [ref:passage:ID].

        ## Citation Format
        - Cite supporting items using [ref:ID] notation, where ID exactly matches a reference from
          the knowledge context (e.g., [ref:fact:1234...]).
        - Place each citation immediately after the claim it supports.
        - Only cite references present in the knowledge context. Never fabricate a reference.
        - A claim without a supporting reference should be omitted or explicitly marked as unsupported.

        ## Truth State Handling
        Facts and relationships carry a truth state. Reflect it faithfully:
        - Confirmed / Likely: present with confidence.
        - Rumor: attribute it as hearsay ("Rumor holds that...", "The party has heard that...").
          Never present a rumor as settled truth.
        - Disputed: present both sides where known, and name the tension ("Accounts conflict...").
        - False: this is recorded misinformation. If asked, say the record marks it false — do not
          repeat it as truth, and do not silently omit it if it answers the question.
        - Hidden: this is GM-only truth included in your context only when the asker may see it.
          When you use it, note that it is not party knowledge ("Known to the GM's record...").

        ## Storylines
        - Artifacts of type Storyline are narrative arcs: mysteries, quests, investigations, threats.
        - Pay attention to their status — Active, Dormant, Resolved, or Archived — and say it when
          relevant ("The Missing Caravan storyline is still open...").
        - When asked what matters or what is unresolved, prefer Active and Dormant storylines.

        ## Answer Style
        - Answer the question first, directly. Add supporting detail after.
        - Keep answers tight: a few short paragraphs at most. No headers, no bullet lists unless the
          question genuinely calls for an enumeration.
        - Plain prose only — your words are rendered as text, not markdown.
        - Stay in the world's register: measured and slightly formal, never theatrical. You are a
          record-keeper, not a bard performing.
        - If the question is a follow-up in a conversation, resolve pronouns and references using
          the conversation history provided.
        """;

    public LoremasterService(
        IKnowledgeRetriever knowledgeRetriever,
        IReferencePassageRetriever passageRetriever,
        ILoremasterAiClient aiClient,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IAiBudgetGuard budgetGuard,
        IOptions<LoremasterOptions> options)
    {
        _knowledgeRetriever = knowledgeRetriever;
        _passageRetriever = passageRetriever;
        _aiClient = aiClient;
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _budgetGuard = budgetGuard;
        _options = options.Value;
    }

    public async Task<AppResult<LoremasterAnswer>> AskAsync(
        AskLoremasterCommand command,
        CancellationToken ct)
    {
        // 1. Validate input
        var validationError = ValidateQuestion(command.Question);
        if (validationError is not null)
            return AppResult<LoremasterAnswer>.Fail(validationError);

        // 1b. Daily AI budget gate — before any retrieval or model work.
        var budgetError = await _budgetGuard.CheckAsync(command.WorldId, ct);
        if (budgetError is not null)
            return AppResult<LoremasterAnswer>.Fail(budgetError);

        // 2. Retrieve knowledge. Follow-up questions often name artifacts only in earlier
        // exchanges ("what about his brother?"), so the conversation context participates
        // in name matching alongside the question itself.
        KnowledgeContext context;
        try
        {
            var retrievalText = string.IsNullOrWhiteSpace(command.ConversationContext)
                ? command.Question
                : $"{command.ConversationContext}\n{command.Question}";

            // Anonymous public asks carry no user; the empty-guid Observer sentinel scopes
            // retrieval to party-visible knowledge exactly as the public browse endpoints do.
            var retrievalUserId = command.UserId ?? Guid.Empty;

            context = await _knowledgeRetriever.RetrieveAsync(
                retrievalText,
                command.WorldId,
                retrievalUserId,
                command.UserRole,
                ct);

            // Library passages ride alongside world memory. The retriever is defensive
            // (returns [] on any failure) and skips embedding when no docs are indexed.
            // Follow-ups name their subject in earlier exchanges, so the conversation
            // context participates in the embedding just as it does in name matching.
            // Public asks opt out entirely — indexed sourcebooks stay off the public site.
            if (command.IncludeLibrary)
            {
                var passages = await _passageRetriever.RetrieveAsync(
                    retrievalText, command.WorldId, retrievalUserId, command.UserRole, ct);
                if (passages.Count > 0)
                {
                    context = new KnowledgeContext
                    {
                        Artifacts = context.Artifacts,
                        Facts = context.Facts,
                        Relationships = context.Relationships,
                        SourceReferences = context.SourceReferences,
                        Passages = passages,
                    };
                }
            }
        }
        catch (Exception)
        {
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(500, "internal_error", "Something went wrong. Please try again."));
        }

        // 3. Build prompt
        var request = BuildPrompt(command.Question, context, command.ConversationContext);

        // 4. Calculate confidence
        var confidence = DetermineConfidence(context);

        // 5. Call AI
        var stopwatch = Stopwatch.StartNew();
        LoremasterAiResponse? aiResponse = null;
        try
        {
            aiResponse = await _aiClient.AskAsync(request, ct);
            stopwatch.Stop();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // propagate real cancellation
        }
        catch (OperationCanceledException)
        {
            // Timeout (TaskCanceledException or OperationCanceledException when not cancelled by token)
            stopwatch.Stop();
            await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, false, "Timeout", ct);
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again."));
        }
        catch (HttpRequestException ex) when (IsRateLimitException(ex))
        {
            // Rate limit (429)
            stopwatch.Stop();
            await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, false, "RateLimited", ct);
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(429, "rate_limited", "Too many requests. Please try again in a moment."));
        }
        catch (HttpRequestException)
        {
            // Service error
            stopwatch.Stop();
            await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, false, "ServiceError", ct);
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again."));
        }
        catch (Exception ex) when (IsRateLimitByTypeName(ex))
        {
            // Rate limit from typed Infrastructure exception
            stopwatch.Stop();
            await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, false, "RateLimited", ct);
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(429, "rate_limited", "Too many requests. Please try again in a moment."));
        }
        catch (Exception ex) when (IsTimeoutByTypeName(ex))
        {
            // Timeout from typed Infrastructure exception
            stopwatch.Stop();
            await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, false, "Timeout", ct);
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again."));
        }
        catch (Exception)
        {
            // Unexpected AI error or service exception
            stopwatch.Stop();
            await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, false, "ServiceError", ct);
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again."));
        }

        // 6. Parse citations from response
        var citations = ParseCitations(aiResponse.AnswerText, context);

        // 7. Assemble caveats
        var caveats = AssembleCaveats(context);

        // 8. Create AiUsageRecord with success
        await TrackUsageAsync(command, aiResponse, stopwatch.Elapsed, true, null, ct);

        // 9. Return LoremasterAnswer
        return AppResult<LoremasterAnswer>.Success(new LoremasterAnswer
        {
            AnswerText = aiResponse.AnswerText,
            Citations = citations,
            Confidence = confidence,
            Caveats = caveats
        });
    }

    /// <summary>
    /// Validates the question input. Returns an AppError if invalid, null if valid.
    /// </summary>
    internal AppError? ValidateQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return new AppError(400, "invalid_question", "Question cannot be empty.");

        if (question.Length > _options.MaxQuestionLength)
            return new AppError(400, "invalid_question",
                $"Question cannot exceed {_options.MaxQuestionLength} characters.");

        return null;
    }

    /// <summary>
    /// Determines confidence level based on the quality and quantity of retrieved knowledge.
    /// </summary>
    internal static ConfidenceLevel DetermineConfidence(KnowledgeContext context)
    {
        if (context.Artifacts.Count == 0)
            return ConfidenceLevel.Low;

        var confirmedFactCount = context.Facts
            .Count(f => f.TruthState is TruthState.Confirmed or TruthState.Likely);

        var totalFactCount = context.Facts.Count;
        var hasRelationships = context.Relationships.Count > 0;
        var hasSourceReferences = context.SourceReferences.Count > 0;

        if (confirmedFactCount >= 3 && hasRelationships && hasSourceReferences)
            return ConfidenceLevel.High;

        if (confirmedFactCount >= 1 || totalFactCount >= 2)
            return ConfidenceLevel.Medium;

        return ConfidenceLevel.Low;
    }

    /// <summary>
    /// Assembles caveats based on the knowledge context.
    /// </summary>
    internal static IReadOnlyList<string> AssembleCaveats(KnowledgeContext context)
    {
        var caveats = new List<string>();

        if (context.Artifacts.Count == 0)
            caveats.Add("Limited information available");

        if (context.Facts.Any(f => f.TruthState == TruthState.Rumor))
            caveats.Add("Some information is marked as rumor");

        if (context.Facts.Any(f => f.TruthState == TruthState.Disputed))
            caveats.Add("Some information is disputed");

        if (context.Facts.Any(f => f.TruthState == TruthState.Hidden))
            caveats.Add("Includes GM-only knowledge not visible to players");

        return caveats;
    }

    private async Task TrackUsageAsync(
        AskLoremasterCommand command,
        LoremasterAiResponse? response,
        TimeSpan elapsed,
        bool succeeded,
        string? errorCode,
        CancellationToken ct)
    {
        var costUsd = CalculateCost(response);

        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            UserId = command.UserId,
            OperationType = AiOperationType.AskLoremaster,
            Model = response?.Model ?? _options.AiModel,
            InputTokens = response?.InputTokens ?? 0,
            OutputTokens = response?.OutputTokens ?? 0,
            TotalTokens = response?.TotalTokens ?? 0,
            EstimatedCostUsd = costUsd,
            DurationMs = (int)elapsed.TotalMilliseconds,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _aiUsageRecordRepository.CreateAsync(record, ct);
    }

    private decimal CalculateCost(LoremasterAiResponse? response)
    {
        if (response is null)
            return 0m;

        if (!_options.ModelPricing.TryGetValue(response.Model, out var pricing))
            return 0m;

        var inputCost = response.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m;
        var outputCost = response.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        return inputCost + outputCost;
    }

    private static bool IsRateLimitException(HttpRequestException ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
        ex.Message.Contains("429", StringComparison.Ordinal) ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private static bool IsRateLimitByTypeName(Exception ex) =>
        ex.GetType().Name.Contains("RateLimit", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimeoutByTypeName(Exception ex) =>
        ex.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the AI request prompt from the question, knowledge context, and optional
    /// prior conversation exchanges.
    /// </summary>
    internal LoremasterAiRequest BuildPrompt(string question, KnowledgeContext context, string? conversationContext = null)
    {
        var userMessage = new StringBuilder();

        var formattedContext = FormatKnowledgeContext(context);
        if (!string.IsNullOrWhiteSpace(formattedContext))
        {
            userMessage.AppendLine("## World Knowledge Context");
            userMessage.AppendLine();
            userMessage.AppendLine(formattedContext);
            userMessage.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(conversationContext))
        {
            userMessage.AppendLine("## Conversation So Far");
            userMessage.AppendLine();
            userMessage.AppendLine(conversationContext);
            userMessage.AppendLine();
        }

        userMessage.AppendLine("## Question");
        userMessage.AppendLine();
        userMessage.AppendLine(question);

        return new LoremasterAiRequest
        {
            SystemPrompt = SystemPromptTemplate,
            UserMessage = userMessage.ToString(),
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };
    }

    /// <summary>
    /// Formats the knowledge context block for inclusion in the AI prompt.
    /// </summary>
    internal static string FormatKnowledgeContext(KnowledgeContext context)
    {
        var hasContent = context.Artifacts.Count > 0
                      || context.Facts.Count > 0
                      || context.Relationships.Count > 0
                      || context.SourceReferences.Count > 0
                      || context.Passages.Count > 0;

        if (!hasContent)
            return string.Empty;

        var sb = new StringBuilder();
        var artifactNames = context.Artifacts.ToDictionary(a => a.Id, a => a.Name);
        var factsByArtifact = context.Facts
            .GroupBy(f => f.ArtifactId)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (context.Artifacts.Count > 0)
        {
            sb.AppendLine("### Artifacts");
            foreach (var artifact in context.Artifacts)
            {
                var status = artifact.Status is not null ? $", {artifact.Status}" : "";
                sb.AppendLine($"- {artifact.Name} ({artifact.Type}{status}): {artifact.Summary ?? "No summary"} [ref:{artifact.ReferenceId}]");

                // Facts belong to their artifact — attribution matters, or "location: Black
                // Harbor" could describe anyone in the context.
                if (factsByArtifact.TryGetValue(artifact.Id, out var artifactFacts))
                {
                    foreach (var fact in artifactFacts)
                    {
                        sb.AppendLine($"  - {fact.Predicate}: {fact.Value}{TruthStateLabel(fact.TruthState)} [ref:{fact.ReferenceId}]");
                    }
                }
            }
            sb.AppendLine();
        }

        // Facts whose artifact wasn't retrieved still carry signal; list them unattributed.
        var orphanFacts = context.Facts.Where(f => !artifactNames.ContainsKey(f.ArtifactId)).ToList();
        if (orphanFacts.Count > 0)
        {
            sb.AppendLine("### Additional Facts");
            foreach (var fact in orphanFacts)
            {
                sb.AppendLine($"- {fact.Predicate}: {fact.Value}{TruthStateLabel(fact.TruthState)} [ref:{fact.ReferenceId}]");
            }
            sb.AppendLine();
        }

        if (context.Relationships.Count > 0)
        {
            sb.AppendLine("### Relationships");
            foreach (var rel in context.Relationships)
            {
                var a = artifactNames.GetValueOrDefault(rel.ArtifactAId, "Unknown artifact");
                var b = artifactNames.GetValueOrDefault(rel.ArtifactBId, "Unknown artifact");
                var description = rel.Description is not null ? $" — {rel.Description}" : "";
                sb.AppendLine($"- {a} <-> {b}: {rel.Type}{description}{TruthStateLabel(rel.TruthState)} [ref:{rel.ReferenceId}]");
            }
            sb.AppendLine();
        }

        if (context.SourceReferences.Count > 0)
        {
            sb.AppendLine("### Source References");
            foreach (var src in context.SourceReferences)
            {
                var quote = src.Quote ?? "(no quote)";
                sb.AppendLine($"- \"{quote}\" [ref:{src.ReferenceId}]");
            }
            sb.AppendLine();
        }

        if (context.Passages.Count > 0)
        {
            sb.AppendLine("### Published Reference (rulebooks and modules — not world canon)");
            foreach (var passage in context.Passages)
            {
                sb.AppendLine($"- From \"{passage.DocumentTitle}\", p. {passage.Page} [ref:{passage.ReferenceId}]:");
                sb.AppendLine($"  {passage.Text.ReplaceLineEndings("\n  ")}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string TruthStateLabel(TruthState truthState) => truthState switch
    {
        TruthState.Rumor => " [Rumor]",
        TruthState.Disputed => " [Disputed]",
        TruthState.False => " [False — recorded misinformation]",
        TruthState.Hidden => " [Hidden — GM-only truth]",
        _ => ""
    };

    /// <summary>
    /// Extracts [ref:ID] citation markers from AI response text and maps them to known
    /// knowledge context items, producing a deduplicated list of Citation objects.
    /// Unknown reference IDs are silently dropped.
    /// </summary>
    internal static IReadOnlyList<Citation> ParseCitations(string responseText, KnowledgeContext context)
    {
        if (string.IsNullOrEmpty(responseText))
            return [];

        var matches = CitationRegex().Matches(responseText);
        if (matches.Count == 0)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var citations = new List<Citation>();

        // Build lookup dictionaries for efficient ID resolution
        var artifactLookup = context.Artifacts.ToDictionary(a => a.ReferenceId, StringComparer.Ordinal);
        var factLookup = context.Facts.ToDictionary(f => f.ReferenceId, StringComparer.Ordinal);
        var relationshipLookup = context.Relationships.ToDictionary(r => r.ReferenceId, StringComparer.Ordinal);
        var sourceLookup = context.SourceReferences.ToDictionary(s => s.ReferenceId, StringComparer.Ordinal);
        var passageLookup = context.Passages.ToDictionary(p => p.ReferenceId, StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            var referenceId = match.Groups[1].Value;

            // Deduplicate by reference ID
            if (!seen.Add(referenceId))
                continue;

            var citation = ResolveCitation(referenceId, artifactLookup, factLookup, relationshipLookup, sourceLookup, passageLookup);
            if (citation is not null)
                citations.Add(citation);
        }

        return citations;
    }

    private static Citation? ResolveCitation(
        string referenceId,
        Dictionary<string, KnowledgeArtifact> artifacts,
        Dictionary<string, KnowledgeFact> facts,
        Dictionary<string, KnowledgeRelationship> relationships,
        Dictionary<string, KnowledgeSourceReference> sources,
        Dictionary<string, KnowledgePassage> passages)
    {
        if (artifacts.TryGetValue(referenceId, out var artifact))
        {
            return new Citation
            {
                ReferenceId = referenceId,
                Type = CitationType.Artifact,
                DisplayName = artifact.Name,
                ArtifactId = artifact.Id
            };
        }

        if (facts.TryGetValue(referenceId, out var fact))
        {
            return new Citation
            {
                ReferenceId = referenceId,
                Type = CitationType.Fact,
                DisplayName = $"{fact.Predicate}: {fact.Value}",
                FactId = fact.Id
            };
        }

        if (relationships.TryGetValue(referenceId, out var relationship))
        {
            return new Citation
            {
                ReferenceId = referenceId,
                Type = CitationType.Relationship,
                DisplayName = relationship.Description ?? relationship.Type,
                RelationshipId = relationship.Id
            };
        }

        if (sources.TryGetValue(referenceId, out var source))
        {
            return new Citation
            {
                ReferenceId = referenceId,
                Type = CitationType.Source,
                DisplayName = source.Quote ?? $"Source reference {source.Id}",
                SourceId = source.Id
            };
        }

        if (passages.TryGetValue(referenceId, out var passage))
        {
            return new Citation
            {
                ReferenceId = referenceId,
                Type = CitationType.Passage,
                DisplayName = $"{passage.DocumentTitle}, p. {passage.Page}",
                DocumentId = passage.DocumentId
            };
        }

        // Unknown reference ID — silently drop
        return null;
    }

    [GeneratedRegex(@"\[ref:([^\]]+)\]")]
    private static partial Regex CitationRegex();
}
