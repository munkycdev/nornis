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
    private readonly ILoremasterAiClient _aiClient;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly LoremasterOptions _options;

    public const string SystemPromptTemplate = """
        You are the Loremaster — a knowledgeable, calm, and trustworthy keeper of campaign knowledge.

        ## Grounding Rules
        - Ground all answers exclusively in the provided campaign knowledge context.
        - Do not invent campaign facts, events, or relationships not present in the context.
        - If the provided context does not contain relevant information, acknowledge this directly.

        ## Citation Format
        - Cite sources using [ref:ID] notation where ID matches a provided reference.
        - Only cite references that are present in the knowledge context.

        ## Truth State Handling
        - When a fact is marked Rumor, qualify the claim (e.g., "Rumor suggests...").
        - When a fact is marked Disputed, note the uncertainty (e.g., "This is disputed, but...").
        - Present Confirmed and Likely facts with confidence.

        ## Anti-Hallucination Instructions
        - Keep answers concise and focused on what the campaign sources support.
        - If unsure, say so rather than fabricating an answer.
        - Never claim certainty about information not present in the context.
        """;

    public LoremasterService(
        IKnowledgeRetriever knowledgeRetriever,
        ILoremasterAiClient aiClient,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IOptions<LoremasterOptions> options)
    {
        _knowledgeRetriever = knowledgeRetriever;
        _aiClient = aiClient;
        _aiUsageRecordRepository = aiUsageRecordRepository;
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

        // 2. Retrieve knowledge
        KnowledgeContext context;
        try
        {
            context = await _knowledgeRetriever.RetrieveAsync(
                command.Question,
                command.CampaignId,
                command.UserId,
                command.UserRole,
                ct);
        }
        catch (Exception)
        {
            return AppResult<LoremasterAnswer>.Fail(
                new AppError(500, "internal_error", "Something went wrong. Please try again."));
        }

        // 3. Build prompt
        var request = BuildPrompt(command.Question, context);

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
            CampaignId = command.CampaignId,
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
    /// Builds the AI request prompt from the question and knowledge context.
    /// </summary>
    internal LoremasterAiRequest BuildPrompt(string question, KnowledgeContext context)
    {
        var userMessage = new StringBuilder();

        var formattedContext = FormatKnowledgeContext(context);
        if (!string.IsNullOrWhiteSpace(formattedContext))
        {
            userMessage.AppendLine("## Campaign Knowledge Context");
            userMessage.AppendLine();
            userMessage.AppendLine(formattedContext);
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
                      || context.SourceReferences.Count > 0;

        if (!hasContent)
            return string.Empty;

        var sb = new StringBuilder();

        if (context.Artifacts.Count > 0)
        {
            sb.AppendLine("### Artifacts");
            foreach (var artifact in context.Artifacts)
            {
                sb.AppendLine($"- {artifact.Name} ({artifact.Type}): {artifact.Summary ?? "No summary"} [ref:{artifact.ReferenceId}]");
            }
            sb.AppendLine();
        }

        if (context.Facts.Count > 0)
        {
            sb.AppendLine("### Facts");
            foreach (var fact in context.Facts)
            {
                var label = fact.TruthState switch
                {
                    TruthState.Rumor => " [Rumor]",
                    TruthState.Disputed => " [Disputed]",
                    _ => ""
                };
                sb.AppendLine($"- {fact.Predicate}: {fact.Value}{label} [ref:{fact.ReferenceId}]");
            }
            sb.AppendLine();
        }

        if (context.Relationships.Count > 0)
        {
            sb.AppendLine("### Relationships");
            foreach (var rel in context.Relationships)
            {
                var label = rel.TruthState switch
                {
                    TruthState.Rumor => " [Rumor]",
                    TruthState.Disputed => " [Disputed]",
                    _ => ""
                };
                var description = rel.Description is not null ? $" — {rel.Description}" : "";
                sb.AppendLine($"- {rel.Type}{description}{label} [ref:{rel.ReferenceId}]");
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

        return sb.ToString();
    }

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

        foreach (Match match in matches)
        {
            var referenceId = match.Groups[1].Value;

            // Deduplicate by reference ID
            if (!seen.Add(referenceId))
                continue;

            var citation = ResolveCitation(referenceId, artifactLookup, factLookup, relationshipLookup, sourceLookup);
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
        Dictionary<string, KnowledgeSourceReference> sources)
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

        // Unknown reference ID — silently drop
        return null;
    }

    [GeneratedRegex(@"\[ref:([^\]]+)\]")]
    private static partial Regex CitationRegex();
}
