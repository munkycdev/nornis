using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Common;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Exceptions;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public interface IArtifactSummaryService
{
    /// <summary>
    /// Regenerates one artifact's summary from its accepted facts and relationships.
    /// <paramref name="requestedAt"/> is the staleness gate: a refresh already stamped past
    /// it was generated against newer state, and this request is a queued duplicate.
    /// </summary>
    Task<ExtractionOutcome> RefreshAsync(
        Guid artifactId, Guid worldId, DateTimeOffset? requestedAt, CancellationToken ct);
}

/// <summary>
/// The accept-time summary maintenance W1 names: the wiki-page half of ingest. Runs as a
/// trusted system operation under ai-extraction.md's carve-out (see its 2026-08-05
/// amendment) — a summary is derived presentation over already-accepted knowledge — unless
/// the world's SummaryReviewRequired routes it through review as a Pending proposal.
///
/// The visibility law does the real work here: the generator reads only facts and
/// relationships visible at the artifact's own scope, through the same ForSourceContext
/// gate extraction uses, hidden truth states included only for GM-only artifacts. A
/// PartyVisible artifact's summary is rendered to every member who can see the page, so
/// GM-only material in its generation context would be a leak, not a nuance.
/// </summary>
public class ArtifactSummaryService : IArtifactSummaryService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly IArtifactSummaryAiClient _aiClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly SyntheticBatchWriter _batchWriter;
    private readonly ExtractionOptions _options;
    private readonly ILogger<ArtifactSummaryService> _logger;

    public ArtifactSummaryService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        IWorldRepository worldRepository,
        IArtifactSummaryAiClient aiClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        SyntheticBatchWriter batchWriter,
        IOptions<ExtractionOptions> options,
        ILogger<ArtifactSummaryService> logger)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _worldRepository = worldRepository;
        _aiClient = aiClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _batchWriter = batchWriter;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The Artifacts.Summary column's own ceiling; a longer generation is cut, not failed.</summary>
    public const int MaxSummaryChars = 2000;

    public async Task<ExtractionOutcome> RefreshAsync(
        Guid artifactId, Guid worldId, DateTimeOffset? requestedAt, CancellationToken ct)
    {
        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
        if (artifact is null)
        {
            // Deleted (or a world cascade took it) between accept and refresh — stale work.
            return ExtractionOutcome.SkippedIdempotent("Artifact no longer exists.");
        }

        if (artifact.WorldId != worldId)
        {
            // Same trust posture as extraction's world assert: a mis-enqueued pair meters
            // one world and touches another, and redelivery reproduces the inconsistency.
            _logger.LogError(
                "Summary refresh world mismatch. ArtifactId={ArtifactId}, MessageWorldId={MessageWorldId}, ArtifactWorldId={ArtifactWorldId}",
                artifactId, worldId, artifact.WorldId);
            return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                "The message's world does not match the artifact's world.");
        }

        if (artifact.Status == ArtifactStatus.Archived)
        {
            return ExtractionOutcome.SkippedIdempotent("Archived artifacts keep their last summary.");
        }

        // Staleness gate: a stamp past the request time means a refresh already ran against
        // state at least as new as the accept that queued this one.
        if (requestedAt is not null && artifact.SummaryRefreshedAt >= requestedAt)
        {
            return ExtractionOutcome.SkippedIdempotent("Summary already refreshed since this was requested.");
        }

        // Empty for legacy rows with no recorded creator: for a Private artifact that means
        // the filter matches no owner at all — strictly narrower, never a leak.
        var filter = VisibilityFilter.ForSourceContext(artifact.Visibility, artifact.CreatedByUserId ?? Guid.Empty);
        var includeHiddenTruths = artifact.Visibility == VisibilityScope.GMOnly;

        var facts = (await _artifactFactRepository.ListByArtifactIdsAsync(
                [artifact.Id], filter, _options.MaxFactsPerArtifact, ct))
            .Where(f => includeHiddenTruths || f.TruthState != TruthState.Hidden)
            .ToList();

        var relationships = await ListVisibleRelationshipsAsync(artifact, filter, ct);

        if (facts.Count == 0 && relationships.Count == 0)
        {
            // Nothing accepted to summarize from — the birth summary (or its absence)
            // stands. Restating a name from a name would be spend without information.
            return ExtractionOutcome.SkippedIdempotent("No accepted facts or relationships in the artifact's scope.");
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            // Complete, don't redeliver: the next accepted change re-requests the refresh
            // after the budget resets, so nothing is wedged behind a spent cap.
            _logger.LogWarning(
                "Summary refresh blocked by AI budget. ArtifactId={ArtifactId}, WorldId={WorldId}", artifactId, worldId);
            return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
        }

        var request = new AiPromptRequest
        {
            SystemPrompt = BuildSystemPrompt(),
            UserMessage = BuildUserMessage(artifact, facts, relationships),
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

        ArtifactSummaryAiResponse response;
        try
        {
            response = await _aiClient.SummarizeAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AiTimeoutException ex)
        {
            await TrackUsageAsync(artifact, null, false, ErrorCategories.Timeout, ct);
            return ExtractionOutcome.Transient(ErrorCategories.Timeout, ex.Message);
        }
        catch (AiParseException ex)
        {
            // One attempt, no parse-retry loop: a failed summary costs nothing user-visible,
            // and the next accepted change asks again.
            await TrackUsageAsync(artifact, ex.Usage, false, ErrorCategories.ParseFailure, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.ParseFailure, ex.Message);
        }
        catch (Exception ex) when (TransientFailureClassifier.IsPermanentHttpFailure(ex))
        {
            _logger.LogError(ex, "Permanent summary refresh failure. ArtifactId={ArtifactId}", artifactId);
            await TrackUsageAsync(artifact, null, false, ErrorCategories.AiCallFailure, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
        }
        catch (Exception ex) when (ex is AiHttpException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Transient summary refresh failure. ArtifactId={ArtifactId}", artifactId);
            await TrackUsageAsync(artifact, null, false, ErrorCategories.TransientError, ct);
            return ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected summary refresh failure. ArtifactId={ArtifactId}", artifactId);
            await TrackUsageAsync(artifact, null, false, ErrorCategories.AiCallFailure, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
        }

        var summary = response.Summary.Trim();
        if (summary.Length == 0)
        {
            await TrackUsageAsync(artifact, response.Usage, false, ErrorCategories.ParseFailure, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.ParseFailure, "The model returned an empty summary.");
        }

        if (summary.Length > MaxSummaryChars)
        {
            summary = summary[..MaxSummaryChars];
        }

        var now = DateTimeOffset.UtcNow;
        var world = await _worldRepository.GetByIdAsync(worldId, ct);

        if (world is { SummaryReviewRequired: true })
        {
            return await FileForReviewAsync(world, artifact, summary, facts.Count, relationships.Count, response, now, ct);
        }

        try
        {
            // Null summary would mean "stamp only" — the review route's write. Here both move
            // together: the text and the provenance stamp are one fact.
            await _artifactRepository.UpdateSummaryAsync(artifact.Id, summary, now, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // An accept just changed this artifact — and its own refresh request is already
            // behind us in the queue, made against the newer state. Skip, don't re-buy.
            await TrackUsageAsync(artifact, response.Usage, true, null, ct);
            return ExtractionOutcome.SkippedIdempotent("A concurrent change superseded this refresh.");
        }

        await TrackUsageAsync(artifact, response.Usage, true, null, ct);

        _logger.LogInformation(
            "Artifact summary refreshed. ArtifactId={ArtifactId}, WorldId={WorldId}, Facts={Facts}, Relationships={Relationships}, Chars={Chars}",
            artifact.Id, worldId, facts.Count, relationships.Count, summary.Length);

        return ExtractionOutcome.Succeeded(Guid.Empty, 0);
    }

    /// <summary>
    /// The per-world review gate: the fresh summary becomes a Pending UpdateArtifact
    /// proposal instead of a direct write. The stamp still moves — "a refresh ran" is true
    /// on this route too, and without it every queued duplicate would file another batch.
    /// </summary>
    private async Task<ExtractionOutcome> FileForReviewAsync(
        World world, Artifact artifact, string summary, int factCount, int relationshipCount,
        ArtifactSummaryAiResponse response, DateTimeOffset now, CancellationToken ct)
    {
        var written = await _batchWriter.WritePendingAsync(
            new SyntheticSourceSpec
            {
                WorldId = artifact.WorldId,
                // The system has no user id of its own; the world's creator owns its
                // provenance sources, as a real FK target that always exists.
                ActingUserId = world.CreatedByUserId,
                Title = $"Summary refresh — {artifact.Name}".Truncate(200),
                Body = $"Regenerated summary for \"{artifact.Name}\" from {factCount} accepted fact(s) and {relationshipCount} relationship(s), for review."
            },
            ReviewBatchKinds.SummaryRefresh,
            [
                new SyntheticProposalSpec
                {
                    ChangeType = ReviewChangeType.UpdateArtifact,
                    TargetType = ReviewTargetType.Artifact,
                    TargetId = artifact.Id,
                    ProposedValueJson = JsonSerializer.Serialize(new { summary }),
                    Rationale = "Accepted changes made the summary stale; this restates the artifact from its current record.",
                    ReferenceNotes = $"Regenerated from {factCount} fact(s) and {relationshipCount} relationship(s)."
                }
            ],
            ct);

        await _artifactRepository.UpdateSummaryAsync(artifact.Id, summary: null, now, ct);
        await TrackUsageAsync(artifact, response.Usage, true, null, ct, written.BatchId);

        _logger.LogInformation(
            "Artifact summary refresh filed for review. ArtifactId={ArtifactId}, BatchId={BatchId}",
            artifact.Id, written.BatchId);

        return ExtractionOutcome.Succeeded(written.BatchId, 1);
    }

    /// <summary>
    /// Relationships in the artifact's scope, with the far endpoint resolved to a name the
    /// artifact's audience may know. An invisible far end drops the whole line — naming a
    /// GM-only artifact in a party-visible summary's basis is the same leak as quoting a
    /// GM-only fact.
    /// </summary>
    private async Task<IReadOnlyList<(ArtifactRelationship Relationship, string OtherName)>> ListVisibleRelationshipsAsync(
        Artifact artifact, VisibilityFilter filter, CancellationToken ct)
    {
        var relationships = await _artifactRelationshipRepository.ListByArtifactIdsAsync([artifact.Id], filter, ct);
        if (relationships.Count == 0)
        {
            return [];
        }

        var otherIds = relationships
            .Select(r => r.ArtifactAId == artifact.Id ? r.ArtifactBId : r.ArtifactAId)
            .Distinct()
            .ToList();

        var othersById = (await _artifactRepository.ListByIdsAsync(otherIds, ct))
            .Where(a => a.WorldId == artifact.WorldId
                && a.Status != ArtifactStatus.Archived
                && filter.CanSee(a.Visibility, a.CreatedByUserId))
            .ToDictionary(a => a.Id);

        var visible = new List<(ArtifactRelationship, string)>();
        foreach (var relationship in relationships)
        {
            var otherId = relationship.ArtifactAId == artifact.Id ? relationship.ArtifactBId : relationship.ArtifactAId;
            if (othersById.TryGetValue(otherId, out var other))
            {
                visible.Add((relationship, other.Name));
            }
        }

        return visible;
    }

    private Task TrackUsageAsync(
        Artifact artifact, AiUsage? usage, bool succeeded, string? errorCode,
        CancellationToken ct, Guid? reviewBatchId = null) =>
        _usageRecorder.RecordAsync(
            artifact.WorldId, null, AiOperationType.ArtifactSummary, usage,
            succeeded, errorCode, reviewBatchId: reviewBatchId,
            fallbackModel: _options.AiModel, ct: ct);

    internal static string BuildSystemPrompt() =>
        """
        You maintain the encyclopedia of a tabletop RPG world. You will be given one
        subject — a character, place, item, faction, event, storyline, concept, or document
        — together with its accepted facts and relationships. Write the subject's summary.

        Rules:
        - Ground every statement in the provided facts and relationships. Invent nothing,
          and bring in nothing you happen to know from elsewhere.
        - Two to four sentences, neutral encyclopedic register, present tense for standing
          truths and past tense for events. No headings, no lists, no markdown.
        - Never refer to the material itself: no "according to the facts", no "the record
          shows". Write as the encyclopedia, not about it.
        - Weigh truth states: state Confirmed material plainly; hedge Likely material
          lightly ("appears to", "is thought to"); attribute Rumor material as rumor; when
          entries genuinely conflict, name the dispute in a clause rather than picking a
          winner. Treat False entries as known misinformation — mention one only if its
          falseness is itself worth recording.
        - Use the subject's proper name early; refer to other artifacts by the names given.
        - A Resolved or Dormant storyline is summarized as concluded or quiet, not ongoing.

        Respond with a JSON object matching the structured output schema: {"summary": "..."}.
        """;

    internal static string BuildUserMessage(
        Artifact artifact,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<(ArtifactRelationship Relationship, string OtherName)> relationships)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Subject");
        sb.AppendLine($"- Name: {artifact.Name}");
        sb.AppendLine($"- Type: {artifact.Type}");
        sb.AppendLine($"- Status: {artifact.Status}");

        if (facts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Accepted facts");
            foreach (var fact in facts)
            {
                sb.AppendLine($"- {fact.Predicate}: {fact.Value} [{fact.TruthState}]");
            }
        }

        if (relationships.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Relationships");
            foreach (var (relationship, otherName) in relationships)
            {
                var description = string.IsNullOrWhiteSpace(relationship.Description)
                    ? string.Empty
                    : $" — {relationship.Description}";
                sb.AppendLine($"- {relationship.Type} {otherName} [{relationship.TruthState}]{description}");
            }
        }

        return sb.ToString();
    }
}
