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
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Backfill sweep for sources extracted before the prompt learned to propose "Advances"
/// (Event→Storyline) and "PartOf" (Storyline→Storyline) links. Re-reads one processed
/// source per message and proposes only those two link types against artifacts that
/// already exist — it never creates artifacts, so there is no duplicate-artifact risk.
/// Each source gets its own review batch (Kind = <see cref="ReviewBatchKinds.RelationshipBackfill"/>) so accepted
/// links carry provenance to the original dated session and light up the timeline at the
/// right spot; the batch is also the per-source idempotency key.
/// </summary>
public class RelationshipBackfillService : IRelationshipBackfillService
{
    public const string AdvancesRelationshipType = "Advances";

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISourceRepository _sourceRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly SyntheticBatchWriter _batchWriter;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly IRelationshipBackfillAiClient _aiClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly ExtractionOptions _options;
    private readonly ILogger<RelationshipBackfillService> _logger;

    public RelationshipBackfillService(
        ISourceRepository sourceRepository,
        IArtifactRepository artifactRepository,
        IArtifactRelationshipRepository relationshipRepository,
        IReviewBatchRepository reviewBatchRepository,
        SyntheticBatchWriter batchWriter,
        IAiUsageRecorder usageRecorder,
        IRelationshipBackfillAiClient aiClient,
        IAiBudgetGuard budgetGuard,
        IOptions<ExtractionOptions> options,
        ILogger<RelationshipBackfillService> logger)
    {
        _sourceRepository = sourceRepository;
        _artifactRepository = artifactRepository;
        _relationshipRepository = relationshipRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _batchWriter = batchWriter;
        _usageRecorder = usageRecorder;
        _aiClient = aiClient;
        _budgetGuard = budgetGuard;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractionOutcome> ProcessBackfillAsync(Guid sourceId, Guid worldId, CancellationToken ct)
    {
        // Idempotency: one backfill batch per source, ever. A redelivered or re-queued
        // message for a swept source is a no-op.
        if (await _reviewBatchRepository.ExistsForSourceAsync(sourceId, ReviewBatchKinds.RelationshipBackfill, ct))
        {
            return ExtractionOutcome.SkippedIdempotent("A relationship backfill batch already exists for this source.");
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null)
        {
            return ExtractionOutcome.NonTransient(Ai.ErrorCategories.SourceNotFound, "Source not found.");
        }

        // The sweep only revisits the historical record; anything still moving through
        // the normal pipeline will get its links from the (now-taught) extraction.
        if (source.ProcessingStatus != SourceProcessingStatus.Processed)
        {
            return ExtractionOutcome.SkippedIdempotent(
                $"Source is in {source.ProcessingStatus} status; only Processed sources are swept.");
        }

        // Sources stored without extraction opted out of the AI pipeline entirely.
        if (!source.ExtractionEnabled)
        {
            return ExtractionOutcome.SkippedIdempotent("Source is stored without extraction.");
        }

        if (source.Type == SourceType.ImportedNote && source.Body is not null)
        {
            source.Body = ImportedNoteNormalizer.Normalize(source.Body);
        }

        if (string.IsNullOrWhiteSpace(source.Body))
        {
            var emptyBatchId = await CreateBatchWithProposalsAsync(source, [], ct);
            return ExtractionOutcome.Succeeded(emptyBatchId, 0);
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            // Complete the message without a batch: the source stays unswept and the GM
            // can re-run the sweep after the budget resets — it picks up where it stopped.
            _logger.LogWarning(
                "Relationship backfill blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}",
                sourceId, worldId);
            return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
        }

        var candidates = await LoadCandidatesAsync(source, worldId, ct);
        if (candidates.Storylines.Count == 0)
        {
            var noLanesBatchId = await CreateBatchWithProposalsAsync(source, [], ct);
            return ExtractionOutcome.Succeeded(noLanesBatchId, 0);
        }

        RelationshipBackfillAiResponse response;
        try
        {
            response = await _aiClient.ProposeLinksAsync(new AiPromptRequest
            {
                SystemPrompt = BuildSystemPrompt(),
                UserMessage = BuildUserMessage(source, candidates),
                Model = _options.AiModel,
                TimeoutSeconds = _options.AiTimeoutSeconds
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            await TrackUsageAsync(source, worldId, null, false, Ai.ErrorCategories.Timeout, ct);
            return ExtractionOutcome.Transient(Ai.ErrorCategories.Timeout, ex.Message);
        }
        catch (HttpRequestException ex) when (TransientFailureClassifier.IsPermanentHttpFailure(ex))
        {
            _logger.LogError(ex, "Permanent AI failure during relationship backfill. SourceId={SourceId}", sourceId);
            await TrackUsageAsync(source, worldId, null, false, Ai.ErrorCategories.AiCallFailure, ct);
            return ExtractionOutcome.NonTransient(Ai.ErrorCategories.AiCallFailure, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Transient AI failure during relationship backfill. SourceId={SourceId}", sourceId);
            await TrackUsageAsync(source, worldId, null, false, Ai.ErrorCategories.TransientError, ct);
            return ExtractionOutcome.Transient(Ai.ErrorCategories.TransientError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected AI failure during relationship backfill. SourceId={SourceId}", sourceId);
            await TrackUsageAsync(source, worldId, null, false, Ai.ErrorCategories.AiCallFailure, ct);
            return ExtractionOutcome.NonTransient(Ai.ErrorCategories.AiCallFailure, ex.Message);
        }

        var accepted = FilterProposals(response.Links, candidates);

        Guid batchId;
        try
        {
            batchId = await CreateBatchWithProposalsAsync(source, accepted, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist backfill proposals. SourceId={SourceId}", sourceId);
            await TrackUsageAsync(source, worldId, response, false, Ai.ErrorCategories.ValidationFailure, ct);
            return ExtractionOutcome.NonTransient(Ai.ErrorCategories.ValidationFailure,
                "Failed to persist backfill proposals: " + ex.Message);
        }

        await TrackUsageAsync(source, worldId, response, true, null, ct, batchId);

        _logger.LogInformation(
            "Relationship backfill complete. SourceId={SourceId}, Proposed={Proposed} (of {Raw} raw), BatchId={BatchId}",
            sourceId, accepted.Count, response.Links.Count, batchId);

        return ExtractionOutcome.Succeeded(batchId, accepted.Count);
    }

    // ------------------------------------------------------------------ Candidates --

    internal sealed record ResolvedLink(
        Artifact ArtifactA, Artifact ArtifactB, string Type,
        string? Description, string Rationale, string? Quote, decimal? Confidence);

    internal sealed record CandidateSet(
        IReadOnlyList<Artifact> Storylines,
        IReadOnlyList<Artifact> Events,
        IReadOnlyList<ArtifactRelationship> ExistingLinks,
        IReadOnlyDictionary<Guid, string> PartOfParentNameByChild);

    private async Task<CandidateSet> LoadCandidatesAsync(Source source, Guid worldId, CancellationToken ct)
    {
        // Same visibility rule as extraction: a PartyVisible source must never see (or
        // link) GM-only material.
        var filter = VisibilityFilter.ForSourceContext(source.Visibility, source.CreatedByUserId);

        var storylines = (await _artifactRepository.ListByWorldAsync(worldId, ArtifactType.Storyline, ct))
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var events = (await _artifactRepository.ListByWorldAsync(worldId, ArtifactType.Event, ct))
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidateIds = storylines.Select(a => a.Id).Concat(events.Select(a => a.Id)).ToList();
        var idSet = candidateIds.ToHashSet();

        var existing = candidateIds.Count == 0
            ? []
            : (await _relationshipRepository.ListByArtifactIdsAsync(candidateIds, filter, ct))
                .Where(r => idSet.Contains(r.ArtifactAId) && idSet.Contains(r.ArtifactBId))
                .ToList();

        var namesById = storylines.Concat(events).ToDictionary(a => a.Id, a => a.Name);
        var parentByChild = existing
            .Where(r => r.Type == ArtifactService.PartOfRelationshipType)
            .DistinctBy(r => r.ArtifactAId)
            .Where(r => namesById.ContainsKey(r.ArtifactBId))
            .ToDictionary(r => r.ArtifactAId, r => namesById[r.ArtifactBId]);

        return new CandidateSet(storylines, events, existing, parentByChild);
    }

    // --------------------------------------------------------------------- Filter --

    internal static IReadOnlyList<ResolvedLink> FilterProposals(
        IReadOnlyList<BackfillLinkProposal> links, CandidateSet candidates)
    {
        var storylinesByName = ToNameLookup(candidates.Storylines);
        var eventsByName = ToNameLookup(candidates.Events);

        // Existing links block re-proposal in either direction for the same type.
        var existingKeys = candidates.ExistingLinks
            .SelectMany(r => new[]
            {
                (r.ArtifactAId, r.ArtifactBId, r.Type),
                (r.ArtifactBId, r.ArtifactAId, r.Type)
            })
            .ToHashSet();

        var accepted = new List<ResolvedLink>();
        var proposedKeys = new HashSet<(Guid, Guid, string)>();

        foreach (var link in links)
        {
            Artifact? a, b;

            switch (link.Type)
            {
                case AdvancesRelationshipType:
                    // A must be the Event, B the Storyline; tolerate the model swapping them.
                    a = ResolveName(eventsByName, link.ArtifactAName) ?? ResolveName(eventsByName, link.ArtifactBName);
                    b = ResolveName(storylinesByName, link.ArtifactBName) ?? ResolveName(storylinesByName, link.ArtifactAName);
                    break;

                case ArtifactService.PartOfRelationshipType:
                    a = ResolveName(storylinesByName, link.ArtifactAName);
                    b = ResolveName(storylinesByName, link.ArtifactBName);
                    // The GM curates the tree: a storyline that already has a parent keeps it.
                    if (a is not null && candidates.PartOfParentNameByChild.ContainsKey(a.Id))
                    {
                        continue;
                    }
                    break;

                default:
                    continue; // only the two taught link types
            }

            if (a is null || b is null || a.Id == b.Id)
            {
                continue;
            }

            var key = (a.Id, b.Id, link.Type);
            if (existingKeys.Contains(key) || existingKeys.Contains((b.Id, a.Id, link.Type)) || !proposedKeys.Add(key))
            {
                continue;
            }

            accepted.Add(new ResolvedLink(a, b, link.Type, link.Description, link.Rationale, link.Quote, link.Confidence));
        }

        return accepted;
    }

    private static Dictionary<string, Artifact> ToNameLookup(IReadOnlyList<Artifact> artifacts)
    {
        // Case-insensitive; a duplicated name is ambiguous, so it resolves to nothing.
        var lookup = new Dictionary<string, Artifact>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts)
        {
            if (!lookup.TryAdd(artifact.Name, artifact))
            {
                ambiguous.Add(artifact.Name);
            }
        }
        foreach (var name in ambiguous)
        {
            lookup.Remove(name);
        }
        return lookup;
    }

    private static Artifact? ResolveName(Dictionary<string, Artifact> lookup, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : lookup.GetValueOrDefault(name.Trim());

    // -------------------------------------------------------------------- Persist --

    private async Task<Guid> CreateBatchWithProposalsAsync(
        Source source, IReadOnlyList<ResolvedLink> links, CancellationToken ct)
    {
        // Zero links still writes: the Completed batch marks the source swept, and the
        // kind is the sweep's per-source idempotency key.
        var specs = links.Select(link => new SyntheticProposalSpec
        {
            ChangeType = ReviewChangeType.AddRelationship,
            TargetType = ReviewTargetType.ArtifactRelationship,
            ProposedValueJson = JsonSerializer.Serialize(new
            {
                artifactAId = link.ArtifactA.Id,
                artifactBId = link.ArtifactB.Id,
                type = link.Type,
                description = link.Description,
                truthState = nameof(TruthState.Confirmed),
                visibility = source.Visibility.ToString(),
                confidence = link.Confidence
            }, PayloadJsonOptions),
            Rationale = link.Rationale.Truncate(500),
            Confidence = link.Confidence,
            ReferenceQuote = link.Quote is null ? null : link.Quote.Truncate(300)
        }).ToList();

        return await _batchWriter.WriteSweepAsync(source, ReviewBatchKinds.RelationshipBackfill, specs, ct);
    }

    // -------------------------------------------------------------------- Prompts --

    internal static string BuildSystemPrompt()
    {
        return """
            You are running a one-time relationship backfill for Nornis, a tabletop RPG world
            memory system. The source below was already extracted into the world record, but at
            the time the extractor did not know how to link Events to Storylines or nest
            Storylines beneath broader arcs. Your only job is to propose those missing links.
            A human reviewer accepts or rejects each one individually.

            Propose exactly two kinds of link, and nothing else:
            - "Advances": when an Event listed under Existing Events advances, opens, or closes a
              Storyline listed under Existing Storylines according to this source, link the Event
              (artifactAName) to that Storyline (artifactBName). An Event that matters almost
              always belongs to an arc — leaving it unlinked hides it from the storyline's timeline.
            - "PartOf": when this source makes a storyline's lineage plain — a side investigation
              opening inside a larger crisis, a quest chain spawning from a campaign arc — link the
              child storyline (artifactAName) to its parent storyline (artifactBName). Both must be
              Storylines. Never propose PartOf for a storyline that already shows "Part of", and
              never guess: the GM curates this tree, so propose lineage only when the source itself
              establishes it.

            Hard rules:
            - Use ONLY names copied character-for-character from the Existing Storylines and
              Existing Events lists. Never invent, abbreviate, or correct a name. If the entity you
              want to link is not in the lists, there is no link to propose.
            - Do not re-propose any link shown under Existing Links.
            - Only propose links this source actually supports. No link is a fine answer.

            For each link give: a one-or-two sentence rationale (max 500 characters) grounded in
            what the source says; a short verbatim quote (under 300 characters) copied from the
            source that best supports the link, or null; and a confidence from 0.0 to 1.0.
            """;
    }

    internal static string BuildUserMessage(Source source, CandidateSet candidates)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Existing Storylines");
        foreach (var storyline in candidates.Storylines)
        {
            var partOf = candidates.PartOfParentNameByChild.TryGetValue(storyline.Id, out var parent)
                ? $" — Part of: {parent}"
                : string.Empty;
            sb.AppendLine($"- {storyline.Name} ({storyline.Status}){partOf}");
            if (!string.IsNullOrWhiteSpace(storyline.Summary))
            {
                sb.AppendLine($"  {storyline.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Existing Events");
        if (candidates.Events.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        foreach (var evt in candidates.Events)
        {
            sb.AppendLine($"- {evt.Name}");
            if (!string.IsNullOrWhiteSpace(evt.Summary))
            {
                sb.AppendLine($"  {evt.Summary}");
            }
        }

        var namesById = candidates.Storylines.Concat(candidates.Events).ToDictionary(a => a.Id, a => a.Name);
        var relevantLinks = candidates.ExistingLinks
            .Where(r => r.Type is AdvancesRelationshipType or ArtifactService.PartOfRelationshipType)
            .ToList();

        sb.AppendLine();
        sb.AppendLine("## Existing Links (do not re-propose)");
        if (relevantLinks.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        foreach (var link in relevantLinks)
        {
            sb.AppendLine($"- {namesById[link.ArtifactAId]} —{link.Type}→ {namesById[link.ArtifactBId]}");
        }

        sb.AppendLine();
        sb.AppendLine($"## Source: {source.Title}");
        if (source.OccurredAt is not null)
        {
            sb.AppendLine($"Session date: {source.OccurredAt:yyyy-MM-dd}");
        }
        sb.AppendLine();
        sb.AppendLine(source.Body);

        return sb.ToString();
    }

    // -------------------------------------------------------------------- Helpers --

    private Task TrackUsageAsync(
        Source source, Guid worldId, RelationshipBackfillAiResponse? response,
        bool succeeded, string? errorCode, CancellationToken ct, Guid? batchId = null) =>
        _usageRecorder.RecordAsync(
            worldId, source.CreatedByUserId, AiOperationType.RelationshipBackfill, response?.Usage,
            succeeded, errorCode, reviewBatchId: batchId, fallbackModel: _options.AiModel, ct: ct);

}
