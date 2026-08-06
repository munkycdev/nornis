using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Exceptions;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ReviewService : IReviewService
{
    /// <summary>
    /// Most open proposals the queue returns in one page. Shared with the nav badge count so the
    /// two agree on when the cap has been reached — a badge reading "200+" beside a queue showing
    /// a different cap would be its own bug report.
    /// </summary>
    public const int ReviewQueueLimit = 200;

    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProposalValidator _proposalValidator;
    private readonly IProposalApplicator _proposalApplicator;

    private readonly IExtractionReplayAdvancer _replayAdvancer;
    private readonly IArtifactSummaryRefreshQueue _summaryRefreshQueue;

    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IReviewProposalRepository reviewProposalRepository,
        IReviewBatchRepository reviewBatchRepository,
        ISourceRepository sourceRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IUnitOfWork unitOfWork,
        IProposalValidator proposalValidator,
        IProposalApplicator proposalApplicator,
        // Required, not optional-with-null: defaulting it meant a host that forgot to register
        // an advancer stalled every replay with nothing to show for it. A caller with no replay
        // passes NoOpExtractionReplayAdvancer.
        IExtractionReplayAdvancer replayAdvancer,
        // Same contract: hosts and tests with no queue pass NoOpArtifactSummaryRefreshQueue.
        IArtifactSummaryRefreshQueue summaryRefreshQueue,
        ILogger<ReviewService>? logger = null)
    {
        // The logger stays optional, deliberately. An absent logger costs log lines, not a
        // feature — which is the whole distinction the parameter above is drawn on.
        _logger = logger ?? NullLogger<ReviewService>.Instance;
        _replayAdvancer = replayAdvancer;
        _summaryRefreshQueue = summaryRefreshQueue;
        _reviewProposalRepository = reviewProposalRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _sourceRepository = sourceRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _unitOfWork = unitOfWork;
        _proposalValidator = proposalValidator;
        _proposalApplicator = proposalApplicator;
    }

    public async Task<AppResult<ReviewQueueResult>> ListReviewQueueAsync(
        ReviewQueueQuery query, CancellationToken ct)
    {
        // Observer sees nothing
        if (query.ActingUserRole == WorldRole.Observer)
            return AppResult<ReviewQueueResult>.Success(new ReviewQueueResult([], false));

        // Validate FilterByBatchId if provided
        if (query.FilterByBatchId is not null)
        {
            var filterBatch = await _reviewBatchRepository.GetByIdAsync(query.FilterByBatchId.Value, ct);
            if (filterBatch is null || filterBatch.WorldId != query.WorldId)
                return AppResult<ReviewQueueResult>.Fail(new AppError(404, "not_found", "Review batch not found."));
        }

        // Compute allowed source IDs based on role and visibility
        var sources = await _sourceRepository.ListByWorldAsync(query.WorldId, cancellationToken: ct);
        var allowedSourceIds = GetAllowedSourceIds(sources, query.ActingUserId, query.ActingUserRole);

        if (allowedSourceIds.Count == 0)
            return AppResult<ReviewQueueResult>.Success(new ReviewQueueResult([], false));

        var (proposals, hasMore) = await _reviewProposalRepository.ListReviewQueueAsync(
            query.WorldId, allowedSourceIds, query.FilterByBatchId, limit: ReviewQueueLimit, ct);

        var context = await BuildProposalContextAsync(query.WorldId, proposals, sources, ct);

        return AppResult<ReviewQueueResult>.Success(new ReviewQueueResult(proposals, hasMore, context));
    }

    /// <summary>
    /// Resolves display context per proposal: the source that produced it (via its batch) and a
    /// human-readable name for what it targets, so the review UI never shows a bare GUID.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, ReviewProposalContext>> BuildProposalContextAsync(
        Guid worldId,
        IReadOnlyList<ReviewProposal> proposals,
        IReadOnlyList<Source> sources,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, ReviewProposalContext>();
        if (proposals.Count == 0)
            return result;

        // Every load below resolves only the ids actually present in this page of
        // proposals — never the world's full batch, artifact, or fact tables. Within the
        // world, name resolution is visibility-unrestricted on purpose: a reviewer only
        // ever receives proposals from sources they may see, and their targets came from
        // that source's own (already-scoped) extraction context. World scope, though, is
        // enforced below — payload-borne ids are Player-editable and must never resolve
        // outside the world. If review ever surfaces proposals across users' sources,
        // this must become a per-user VisibilityFilter.
        var batchIds = proposals.Select(p => p.ReviewBatchId).Distinct().ToList();
        var batches = (await _reviewBatchRepository.ListByIdsAsync(batchIds, ct))
            .ToDictionary(b => b.Id);
        var sourceTitles = sources.ToDictionary(s => s.Id, s => s.Title);

        var mergePayloads = proposals
            .Where(p => p.ChangeType == ReviewChangeType.MergeArtifact)
            .Select(p => (p.Id, Payload: DeserializePayload<MergeArtifactPayload>(p.ProposedValueJson)))
            .Where(x => x.Payload is not null)
            .ToDictionary(x => x.Id, x => x.Payload!);

        var addRelationshipPayloads = proposals
            .Where(p => p.ChangeType == ReviewChangeType.AddRelationship)
            .Select(p => (p.Id, Payload: DeserializePayload<AddRelationshipPayload>(p.ProposedValueJson)))
            .Where(x => x.Payload is not null)
            .ToDictionary(x => x.Id, x => x.Payload!);

        var factIds = proposals
            .Where(p => p.ChangeType == ReviewChangeType.UpdateFact && p.TargetId is not null)
            .Select(p => p.TargetId!.Value)
            .Distinct()
            .ToList();
        var factsById = factIds.Count > 0
            ? (await _artifactFactRepository.ListByIdsAsync(factIds, ct)).ToDictionary(f => f.Id)
            : null;

        var relationshipIds = proposals
            .Where(p => p.ChangeType == ReviewChangeType.UpdateRelationship && p.TargetId is not null)
            .Select(p => p.TargetId!.Value)
            .Distinct()
            .ToList();
        var relationshipsById = relationshipIds.Count > 0
            ? (await _artifactRelationshipRepository.ListByIdsAsync(relationshipIds, ct)).ToDictionary(r => r.Id)
            : null;

        // The artifact names a page can mention: direct targets, merge sources, the owners
        // of updated facts, and the endpoints of added or updated relationships.
        var artifactIds = new HashSet<Guid>();
        foreach (var proposal in proposals)
        {
            if (proposal.TargetId is { } targetId && proposal.ChangeType
                is ReviewChangeType.AddFact or ReviewChangeType.UpdateArtifact or ReviewChangeType.MergeArtifact)
            {
                artifactIds.Add(targetId);
            }
        }
        foreach (var payload in mergePayloads.Values)
            artifactIds.Add(payload.SourceArtifactId);
        foreach (var payload in addRelationshipPayloads.Values)
        {
            if (payload.ArtifactAId is { } aId)
                artifactIds.Add(aId);
            if (payload.ArtifactBId is { } bId)
                artifactIds.Add(bId);
        }
        if (factsById is not null)
        {
            foreach (var fact in factsById.Values)
                artifactIds.Add(fact.ArtifactId);
        }
        if (relationshipsById is not null)
        {
            foreach (var relationship in relationshipsById.Values)
            {
                artifactIds.Add(relationship.ArtifactAId);
                artifactIds.Add(relationship.ArtifactBId);
            }
        }

        // World-scoped: the merge and relationship ids come out of Player-editable
        // payloads, so an unscoped by-ids load would let an edited GUID read another
        // world's artifact name — or probe existence — through the review queue.
        var artifactNames = artifactIds.Count > 0
            ? (await _artifactRepository.ListByIdsAsync(artifactIds.ToList(), ct))
                .Where(a => a.WorldId == worldId)
                .ToDictionary(a => a.Id, a => a.Name)
            : [];

        foreach (var proposal in proposals)
        {
            Guid sourceId = default;
            var sourceTitle = "Unknown source";
            string? batchKind = null;
            if (batches.TryGetValue(proposal.ReviewBatchId, out var batch))
            {
                sourceId = batch.SourceId;
                sourceTitle = sourceTitles.GetValueOrDefault(batch.SourceId, sourceTitle);
                batchKind = batch.Kind;
            }

            var targetName = ResolveTargetName(
                proposal, artifactNames, factsById, relationshipsById, addRelationshipPayloads);

            string? mergeSourceName = null;
            if (mergePayloads.TryGetValue(proposal.Id, out var mergePayload)
                && artifactNames.TryGetValue(mergePayload.SourceArtifactId, out var name))
            {
                mergeSourceName = name;
            }

            result[proposal.Id] = new ReviewProposalContext(sourceId, sourceTitle, targetName, mergeSourceName, batchKind);
        }

        return result;
    }

    private static string? ResolveTargetName(
        ReviewProposal proposal,
        Dictionary<Guid, string> artifactNames,
        Dictionary<Guid, ArtifactFact>? factsById,
        Dictionary<Guid, ArtifactRelationship>? relationshipsById,
        Dictionary<Guid, AddRelationshipPayload> addRelationshipPayloads)
    {
        switch (proposal.ChangeType)
        {
            // AddFact's TargetId references the owning artifact; when it is null the payload
            // carries a same-batch artifactName instead.
            case ReviewChangeType.AddFact:
                if (proposal.TargetId is { } factArtifactId)
                    return artifactNames.GetValueOrDefault(factArtifactId);
                return DeserializePayload<AddFactPayload>(proposal.ProposedValueJson)?.ArtifactName;

            case ReviewChangeType.UpdateArtifact:
            case ReviewChangeType.MergeArtifact:
                return proposal.TargetId is { } artifactId
                    ? artifactNames.GetValueOrDefault(artifactId)
                    : null;

            case ReviewChangeType.UpdateFact:
                if (proposal.TargetId is { } factId && factsById?.GetValueOrDefault(factId) is { } fact)
                {
                    var owner = artifactNames.GetValueOrDefault(fact.ArtifactId, "Unknown artifact");
                    return $"{owner} — {fact.Predicate}";
                }
                return null;

            case ReviewChangeType.AddRelationship:
                {
                    if (!addRelationshipPayloads.TryGetValue(proposal.Id, out var payload))
                        return null;
                    var a = payload.ArtifactAId is { } aId
                        ? artifactNames.GetValueOrDefault(aId, payload.ArtifactAName ?? "?")
                        : payload.ArtifactAName ?? "?";
                    var b = payload.ArtifactBId is { } bId
                        ? artifactNames.GetValueOrDefault(bId, payload.ArtifactBName ?? "?")
                        : payload.ArtifactBName ?? "?";
                    return $"{a} ↔ {b}";
                }

            case ReviewChangeType.UpdateRelationship:
                if (proposal.TargetId is { } relId && relationshipsById?.GetValueOrDefault(relId) is { } rel)
                {
                    var a = artifactNames.GetValueOrDefault(rel.ArtifactAId, "?");
                    var b = artifactNames.GetValueOrDefault(rel.ArtifactBId, "?");
                    return $"{a} ↔ {b}";
                }
                return null;

            default:
                return null; // CreateArtifact: the payload's own name is already displayed
        }
    }

    private static T? DeserializePayload<T>(string json) where T : class
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, PayloadJsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // Same numeric-string tolerance as the validator and applicator: a quoted confidence
    // must not blank out the queue's target names.
    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public Task<AppResult<AcceptProposalResult>> AcceptProposalAsync(
        AcceptProposalCommand command, CancellationToken ct) =>
        AcceptProposalCoreAsync(command, allowCascade: true, ct);

    /// <summary>
    /// <paramref name="allowCascade"/>: when a name-referenced accept fails because its
    /// artifact only exists as a sibling Create proposal, accept those prerequisites and
    /// retry once — so accept order within a batch never matters to the reviewer. The
    /// retry and the prerequisites themselves run with cascading off (no loops).
    /// </summary>
    private async Task<AppResult<AcceptProposalResult>> AcceptProposalCoreAsync(
        AcceptProposalCommand command, bool allowCascade, CancellationToken ct)
    {
        var loaded = await LoadAuthorizedProposalAsync(
            command.ProposalId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
        if (!loaded.IsSuccess)
            return AppResult<AcceptProposalResult>.Fail(loaded.Error!);
        var (proposal, batch, source) = loaded.Value!;

        // Terminal states: accept is idempotent, rejected conflicts. The enum has no
        // fifth member, so everything else is Pending or Edited and proceeds.
        switch (proposal.Status)
        {
            case ReviewProposalStatus.Accepted:
                return AppResult<AcceptProposalResult>.Success(new AcceptProposalResult(
                    proposal.Id, proposal.Status, proposal.ReviewedAt!.Value, proposal.ReviewedByUserId!.Value,
                    proposal.TargetId, proposal.AppliedToExistingArtifact == true));
            case ReviewProposalStatus.Rejected:
                return AppResult<AcceptProposalResult>.Fail(new AppError(409, "conflict", "Cannot accept a proposal that has already been rejected."));
        }

        // Validate ProposedValueJson
        var validationResult = _proposalValidator.ValidateProposedValue(proposal.ProposedValueJson, proposal.ChangeType);
        if (!validationResult.IsSuccess)
            return AppResult<AcceptProposalResult>.Fail(validationResult.Error!);

        // Begin transaction
        var matchedExisting = false;
        IReadOnlyList<Guid> summaryCandidates = [];
        IReadOnlyList<Guid> summaryPinned = [];
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Name-referenced payloads resolve through the accepter's own eyes: a Player
            // reviewing their own source must not bind a fact to a GM-only or other-user
            // Private artifact.
            var actingFilter = VisibilityFilter.ForRole(command.ActingUserRole, command.ActingUserId);

            var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, source, actingFilter, ct);
            if (!applyResult.IsSuccess)
            {
                await transaction.RollbackAsync(ct);

                if (allowCascade && applyResult.Error!.Code == "artifact_name_not_found")
                {
                    return await AcceptWithPrerequisitesAsync(command, proposal, batch, ct);
                }

                return AppResult<AcceptProposalResult>.Fail(applyResult.Error!);
            }

            matchedExisting = applyResult.Value!.MatchedExistingArtifact;
            summaryCandidates = applyResult.Value.SummaryRefreshCandidates;
            summaryPinned = applyResult.Value.SummaryPinnedArtifactIds;

            var now = DateTimeOffset.UtcNow;
            proposal.Accept(command.ActingUserId, now);
            await _reviewProposalRepository.UpdateAsync(proposal, ct);

            await transaction.CommitAsync(ct);
        }
        catch (ConcurrencyConflictException ex)
        {
            // Two accepts of the same proposal raced. This is a conflict, not a server
            // fault: the loser used to get an unlogged 500 saying the operation could not
            // be completed, when in truth someone else completed it.
            await transaction.RollbackAsync(ct);
            _logger.LogInformation(ex,
                "Concurrent accept lost the race. ProposalId={ProposalId}", proposal.Id);
            return AppResult<AcceptProposalResult>.Fail(new AppError(409, "conflict",
                "This proposal was decided by another request. Refresh to see where it stands."));
        }
        catch (Exception ex)
        {
            // Still broad — an apply touches several repositories and half a write must not
            // escape the rollback. But it is logged now: this used to convert bugs,
            // constraint violations and conflicts alike into one identical, untraceable
            // message, which is why the race above went unnoticed.
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex,
                "Accept transaction failed. ProposalId={ProposalId}", proposal.Id);
            return AppResult<AcceptProposalResult>.Fail(
                new AppError(500, "transaction_failed", $"Failed to accept proposal {proposal.Id}. The operation could not be completed."));
        }

        // Batch lifecycle OUTSIDE transaction
        await UpdateBatchLifecycleAsync(batch.Id, ct);

        var refresh = summaryCandidates.Except(summaryPinned).ToList();
        if (refresh.Count > 0)
        {
            await _summaryRefreshQueue.TryEnqueueAsync(command.WorldId, refresh, ct);
        }

        return AppResult<AcceptProposalResult>.Success(new AcceptProposalResult(
            proposal.Id, proposal.Status, proposal.ReviewedAt!.Value, proposal.ReviewedByUserId!.Value,
            proposal.TargetId, matchedExisting));
    }

    /// <summary>
    /// The failed proposal references artifacts by name that don't exist yet. If the same
    /// batch holds undecided Create proposals for those names, accept them first, then
    /// retry the original — turning "accept its Create proposal first" from an error the
    /// reviewer must untangle into something the pipeline just does.
    /// </summary>
    private async Task<AppResult<AcceptProposalResult>> AcceptWithPrerequisitesAsync(
        AcceptProposalCommand command, ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        var referenced = ExtractReferencedArtifactNames(proposal.ProposedValueJson);
        var siblings = await _reviewProposalRepository.ListByReviewBatchAsync(batch.Id, ct);

        var prerequisites = siblings
            .Where(s => s.ChangeType == ReviewChangeType.CreateArtifact
                && s.Status is ReviewProposalStatus.Pending or ReviewProposalStatus.Edited
                && ExtractCreateName(s.ProposedValueJson) is { } name
                && referenced.Contains(name))
            .ToList();

        if (prerequisites.Count == 0)
        {
            // Nothing in the batch can satisfy the reference; surface the original error.
            return AppResult<AcceptProposalResult>.Fail(new AppError(404, "artifact_name_not_found",
                "The proposal references an artifact that does not exist and is not proposed in this batch."));
        }

        foreach (var prerequisite in prerequisites)
        {
            var prereqResult = await AcceptProposalCoreAsync(
                command with { ProposalId = prerequisite.Id }, allowCascade: false, ct);
            if (!prereqResult.IsSuccess)
            {
                return AppResult<AcceptProposalResult>.Fail(prereqResult.Error!);
            }
        }

        return await AcceptProposalCoreAsync(command, allowCascade: false, ct);
    }

    private static HashSet<string> ExtractReferencedArtifactNames(string proposedValueJson)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(proposedValueJson);
            foreach (var key in (string[])["artifactName", "artifactAName", "artifactBName"])
            {
                if (doc.RootElement.TryGetProperty(key, out var el)
                    && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                {
                    names.Add(s.Trim());
                }
            }
        }
        catch (JsonException)
        {
            // Unparseable payloads already failed validation upstream.
        }

        return names;
    }

    private static string? ExtractCreateName(string proposedValueJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(proposedValueJson);
            return doc.RootElement.TryGetProperty("name", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<AppResult<RejectProposalResult>> RejectProposalAsync(
        RejectProposalCommand command, CancellationToken ct)
    {
        var loaded = await LoadAuthorizedProposalAsync(
            command.ProposalId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
        if (!loaded.IsSuccess)
            return AppResult<RejectProposalResult>.Fail(loaded.Error!);
        var (proposal, batch, _) = loaded.Value!;

        // Terminal states: reject is idempotent, accepted conflicts. The enum has no
        // fifth member, so everything else is Pending or Edited and proceeds.
        switch (proposal.Status)
        {
            case ReviewProposalStatus.Rejected:
                return AppResult<RejectProposalResult>.Success(new RejectProposalResult(
                    proposal.Id, proposal.Status, proposal.ReviewedAt!.Value, proposal.ReviewedByUserId!.Value));
            case ReviewProposalStatus.Accepted:
                return AppResult<RejectProposalResult>.Fail(new AppError(409, "conflict", "Cannot reject a proposal that has already been accepted."));
        }

        var now = DateTimeOffset.UtcNow;
        proposal.Reject(command.ActingUserId, now);
        await _reviewProposalRepository.UpdateAsync(proposal, ct);

        await UpdateBatchLifecycleAsync(batch.Id, ct);

        return AppResult<RejectProposalResult>.Success(new RejectProposalResult(
            proposal.Id, proposal.Status, now, command.ActingUserId));
    }

    public async Task<AppResult<EditProposalResult>> EditProposalAsync(
        EditProposalCommand command, CancellationToken ct)
    {
        var loaded = await LoadAuthorizedProposalAsync(
            command.ProposalId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
        if (!loaded.IsSuccess)
        {
            return AppResult<EditProposalResult>.Fail(loaded.Error!);
        }
        var (proposal, _, _) = loaded.Value!;

        // Terminal states conflict; the enum has no fifth member, so everything else is
        // Pending or Edited and proceeds.
        if (proposal.Status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected)
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(409, "conflict", "Only Pending or Edited proposals can be edited."));
        }

        var validationResult = _proposalValidator.ValidateProposedValue(
            command.NewProposedValueJson, proposal.ChangeType);
        if (!validationResult.IsSuccess)
        {
            return AppResult<EditProposalResult>.Fail(validationResult.Error!);
        }

        proposal.ProposedValueJson = command.NewProposedValueJson;

        var now = DateTimeOffset.UtcNow;
        proposal.MarkEdited(command.ActingUserId, now);

        await _reviewProposalRepository.UpdateAsync(proposal, ct);

        await UpdateBatchLifecycleAsync(proposal.ReviewBatchId, ct);

        return AppResult<EditProposalResult>.Success(
            new EditProposalResult(
                proposal.Id,
                ReviewProposalStatus.Edited,
                proposal.ProposedValueJson,
                now,
                command.ActingUserId));
    }

    public async Task<AppResult<BatchOperationResult>> BatchAcceptAsync(
        BatchAcceptCommand command, CancellationToken ct)
    {
        if (command.ProposalIds.Count < 1 || command.ProposalIds.Count > 50)
            return AppResult<BatchOperationResult>.Fail(
                new AppError(400, "validation_error", "Batch size must be between 1 and 50 proposals."));

        var uniqueIds = command.ProposalIds.Distinct().ToList();
        var succeeded = new List<Guid>();
        var failed = new List<BatchFailureDetail>();
        var affectedBatchIds = new HashSet<Guid>();

        // Coalesced across the whole accept: one refresh request per artifact no matter how
        // many proposals touched it — and pinned subtracts from the whole set, because an
        // explicit summary in proposal 3 must also cancel the refresh proposals 1 and 2
        // asked for on the same artifact.
        var summaryCandidates = new HashSet<Guid>();
        var summaryPinned = new HashSet<Guid>();

        // A batch accept is nearly always 50 proposals from ONE extraction, so the batch and its
        // source were being re-read up to 50 times each. Both are immutable for the duration of
        // this operation — nothing here writes to either — so they are read once and reused.
        //
        // The PROPOSAL is deliberately not memoised: TryAcceptOneAsync re-reads it so the retry
        // pass below sees the updated Status and takes the idempotent path instead of applying a
        // second time.
        var batches = new Dictionary<Guid, ReviewBatch>();
        var sources = new Dictionary<Guid, Source>();

        foreach (var proposalId in await OrderCreatesFirstAsync(uniqueIds, ct))
        {
            var failure = await TryAcceptOneAsync(
                proposalId, command, succeeded, affectedBatchIds, batches, sources,
                summaryCandidates, summaryPinned, ct);
            if (failure is not null)
                failed.Add(failure);
        }

        // Belt-and-braces: an odd intra-batch shape (a create that only became applicable
        // once one of its siblings landed) can still leave a name unresolved after the
        // ordered pass. Retry those exactly once — no more, or a genuinely unresolvable
        // name would spin. Everything else keeps its original error.
        var retryIds = failed
            .Where(f => f.Code == "artifact_name_not_found")
            .Select(f => f.ProposalId)
            .ToList();

        foreach (var proposalId in retryIds)
        {
            var failure = await TryAcceptOneAsync(
                proposalId, command, succeeded, affectedBatchIds, batches, sources,
                summaryCandidates, summaryPinned, ct);

            // Only a success changes anything: the proposal moves out of failed. A second
            // failure keeps the detail already recorded.
            if (failure is null)
                failed.RemoveAll(f => f.ProposalId == proposalId);
        }

        // Batch lifecycle OUTSIDE individual transactions
        foreach (var batchId in affectedBatchIds)
        {
            await UpdateBatchLifecycleAsync(batchId, ct);
        }

        summaryCandidates.ExceptWith(summaryPinned);
        if (summaryCandidates.Count > 0)
        {
            await _summaryRefreshQueue.TryEnqueueAsync(command.WorldId, summaryCandidates, ct);
        }

        return AppResult<BatchOperationResult>.Success(new BatchOperationResult(succeeded, failed));
    }

    /// <summary>
    /// CreateArtifact proposals first, everything else after, each group in the caller's
    /// order. Facts and relationships reference same-batch artifacts by name, and those
    /// names only resolve once the create has been applied — so a selection listing the
    /// fact before its create used to half-fail on nothing but ordering.
    ///
    /// Unlike single accept, this deliberately does NOT pull in creates the user did not
    /// select: batch accept applies exactly the chosen set, and a fact whose prerequisite
    /// is neither in canon nor in the selection fails with the same error as before.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> OrderCreatesFirstAsync(
        IReadOnlyList<Guid> proposalIds, CancellationToken ct)
    {
        // One query for the whole selection rather than one per id. This reads nothing but
        // ChangeType and is used only for ordering — the accept path re-reads each proposal for
        // itself, deliberately (see TryAcceptOneAsync), so nothing here can go stale in a way
        // that matters.
        var creates = (await _reviewProposalRepository.ListByIdsAsync(proposalIds, ct))
            .Where(p => p.ChangeType == ReviewChangeType.CreateArtifact)
            .Select(p => p.Id)
            .ToHashSet();

        // OrderBy is stable, so relative order inside each group is the caller's.
        return proposalIds.OrderBy(id => creates.Contains(id) ? 0 : 1).ToList();
    }

    /// <summary>
    /// One proposal's worth of batch accept. Returns null when it succeeded (having recorded
    /// the id and its batch), or the failure detail to report. Re-reads the proposal, so it
    /// is safe to call a second time for the retry pass: an already-Accepted proposal takes
    /// the idempotent path instead of applying twice.
    /// </summary>
    private async Task<BatchFailureDetail?> TryAcceptOneAsync(
        Guid proposalId,
        BatchAcceptCommand command,
        List<Guid> succeeded,
        HashSet<Guid> affectedBatchIds,
        Dictionary<Guid, ReviewBatch> batches,
        Dictionary<Guid, Source> sources,
        HashSet<Guid> summaryCandidates,
        HashSet<Guid> summaryPinned,
        CancellationToken ct)
    {
        var loadResult = await LoadAuthorizedProposalAsync(
            proposalId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct,
            batches, sources);
        if (!loadResult.IsSuccess)
            return new BatchFailureDetail(proposalId, loadResult.Error!.Code, loadResult.Error!.Message);
        var (proposal, batch, source) = loadResult.Value!;

        // Idempotent: already Accepted
        if (proposal.Status == ReviewProposalStatus.Accepted)
        {
            if (!succeeded.Contains(proposalId))
                succeeded.Add(proposalId);
            affectedBatchIds.Add(batch.Id);
            return null;
        }

        // Conflicting: Rejected → conflict. The enum has no fifth member, so everything
        // else is Pending or Edited and proceeds.
        if (proposal.Status == ReviewProposalStatus.Rejected)
            return new BatchFailureDetail(proposalId, "conflict", "Cannot accept a proposal that has already been rejected.");

        // Validate ProposedValueJson
        var validationResult = _proposalValidator.ValidateProposedValue(proposal.ProposedValueJson, proposal.ChangeType);
        if (!validationResult.IsSuccess)
            return new BatchFailureDetail(proposalId, validationResult.Error!.Code, validationResult.Error!.Message);

        // Begin transaction per proposal
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var actingFilter = VisibilityFilter.ForRole(command.ActingUserRole, command.ActingUserId);

            var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, source, actingFilter, ct);
            if (!applyResult.IsSuccess)
            {
                await transaction.RollbackAsync(ct);
                return new BatchFailureDetail(proposalId, applyResult.Error!.Code, applyResult.Error!.Message);
            }

            var now = DateTimeOffset.UtcNow;
            proposal.Accept(command.ActingUserId, now);
            await _reviewProposalRepository.UpdateAsync(proposal, ct);

            await transaction.CommitAsync(ct);

            // After the commit, so a rolled-back apply leaves no refresh request behind.
            summaryCandidates.UnionWith(applyResult.Value!.SummaryRefreshCandidates);
            summaryPinned.UnionWith(applyResult.Value.SummaryPinnedArtifactIds);
        }
        catch (ConcurrencyConflictException ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogInformation(ex,
                "Concurrent accept lost the race in a batch. ProposalId={ProposalId}", proposalId);
            return new BatchFailureDetail(proposalId, "conflict",
                "This proposal was decided by another request.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex,
                "Batch accept transaction failed. ProposalId={ProposalId}", proposalId);
            return new BatchFailureDetail(proposalId, "transaction_failed", $"Failed to accept proposal {proposalId}. The operation could not be completed.");
        }

        if (!succeeded.Contains(proposalId))
            succeeded.Add(proposalId);
        affectedBatchIds.Add(batch.Id);
        return null;
    }

    public async Task<AppResult<BatchOperationResult>> BatchRejectAsync(
        BatchRejectCommand command, CancellationToken ct)
    {
        if (command.ProposalIds.Count < 1 || command.ProposalIds.Count > 50)
            return AppResult<BatchOperationResult>.Fail(
                new AppError(400, "validation_error", "Batch size must be between 1 and 50 proposals."));

        var uniqueIds = command.ProposalIds.Distinct().ToList();
        var succeeded = new List<Guid>();
        var failed = new List<BatchFailureDetail>();
        var affectedBatchIds = new HashSet<Guid>();

        // Same memoization batch accept carries: the selection is nearly always one
        // extraction's proposals, and the batch and source are immutable for the
        // duration — read once, reused across the loop.
        var batches = new Dictionary<Guid, ReviewBatch>();
        var sources = new Dictionary<Guid, Source>();

        foreach (var proposalId in uniqueIds)
        {
            var loadResult = await LoadAuthorizedProposalAsync(
                proposalId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct,
                batches, sources);
            if (!loadResult.IsSuccess)
            {
                failed.Add(new BatchFailureDetail(proposalId, loadResult.Error!.Code, loadResult.Error!.Message));
                continue;
            }
            var (proposal, batch, _) = loadResult.Value!;

            // Idempotent: already Rejected
            if (proposal.Status == ReviewProposalStatus.Rejected)
            {
                succeeded.Add(proposalId);
                affectedBatchIds.Add(batch.Id);
                continue;
            }

            // Conflicting: Accepted → conflict. The enum has no fifth member, so
            // everything else is Pending or Edited and proceeds.
            if (proposal.Status == ReviewProposalStatus.Accepted)
            {
                failed.Add(new BatchFailureDetail(proposalId, "conflict", "Cannot reject a proposal that has already been accepted."));
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            proposal.Reject(command.ActingUserId, now);
            await _reviewProposalRepository.UpdateAsync(proposal, ct);

            succeeded.Add(proposalId);
            affectedBatchIds.Add(batch.Id);
        }

        // Batch lifecycle OUTSIDE individual proposal processing
        foreach (var batchId in affectedBatchIds)
        {
            await UpdateBatchLifecycleAsync(batchId, ct);
        }

        return AppResult<BatchOperationResult>.Success(new BatchOperationResult(succeeded, failed));
    }

    private static IReadOnlyList<Guid> GetAllowedSourceIds(
        IReadOnlyList<Source> worldSources, Guid userId, WorldRole role)
    {
        return role switch
        {
            WorldRole.GM => worldSources.Select(s => s.Id).ToList(),
            WorldRole.Player => worldSources
                .Where(s => s.CreatedByUserId == userId)
                .Select(s => s.Id).ToList(),
            WorldRole.Observer => [],
            _ => []
        };
    }

    private sealed record AuthorizedProposal(ReviewProposal Proposal, ReviewBatch Batch, Source Source);

    /// <summary>
    /// The proposal → batch → source ladder every review action starts with, authorization
    /// included. Every rung fails as the same 404 — whether the proposal is missing,
    /// addressed through the wrong world, orphaned of its batch or source, or backed by a
    /// source this caller may not review, the caller learns only "no such proposal". Role
    /// failures surface as the 403 <see cref="CheckReviewAuthorization"/> produces.
    ///
    /// The proposal itself is deliberately re-read on every call — batch accept's retry
    /// pass depends on seeing the updated Status so an already-accepted proposal takes the
    /// idempotent path instead of applying twice. Batch operations pass their per-command
    /// caches for the batch and source, which are immutable for the operation; an entry is
    /// only ever cached after its world check passed, so a cached batch can never be served
    /// to a later proposal unchecked.
    /// </summary>
    private async Task<AppResult<AuthorizedProposal>> LoadAuthorizedProposalAsync(
        Guid proposalId,
        Guid worldId,
        Guid actingUserId,
        WorldRole actingRole,
        CancellationToken ct,
        Dictionary<Guid, ReviewBatch>? batchCache = null,
        Dictionary<Guid, Source>? sourceCache = null)
    {
        var notFound = new AppError(404, "not_found", "Proposal not found.");

        var proposal = await _reviewProposalRepository.GetByIdAsync(proposalId, ct);
        if (proposal is null)
            return AppResult<AuthorizedProposal>.Fail(notFound);

        if (batchCache is null || !batchCache.TryGetValue(proposal.ReviewBatchId, out var batch))
        {
            batch = await _reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId, ct);
            if (batch is null || batch.WorldId != worldId)
                return AppResult<AuthorizedProposal>.Fail(notFound);
            batchCache?.Add(proposal.ReviewBatchId, batch);
        }

        if (sourceCache is null || !sourceCache.TryGetValue(batch.SourceId, out var source))
        {
            source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
            if (source is null)
                return AppResult<AuthorizedProposal>.Fail(notFound);
            sourceCache?.Add(batch.SourceId, source);
        }

        if (!CanReviewSource(source, actingUserId, actingRole))
            return AppResult<AuthorizedProposal>.Fail(notFound);

        var authResult = CheckReviewAuthorization(actingRole, actingUserId, source);
        if (!authResult.IsSuccess)
            return AppResult<AuthorizedProposal>.Fail(authResult.Error!);

        return AppResult<AuthorizedProposal>.Success(new AuthorizedProposal(proposal, batch, source));
    }

    // Deliberately NOT SourceVisibilityRule: review authorization is narrower than
    // visibility. A Player can see any PartyVisible source but may only act on
    // proposals from sources they authored.
    private bool CanReviewSource(Source source, Guid userId, WorldRole role)
    {
        return role switch
        {
            WorldRole.GM => true,
            WorldRole.Player => source.CreatedByUserId == userId,
            WorldRole.Observer => false,
            _ => false
        };
    }

    private static AppResult CheckReviewAuthorization(WorldRole role, Guid actingUserId, Source source)
    {
        return role switch
        {
            WorldRole.Observer => AppResult.Fail(
                new AppError(403, "forbidden", "Observers cannot review proposals.")),
            WorldRole.Player when source.CreatedByUserId != actingUserId => AppResult.Fail(
                new AppError(403, "forbidden", "Players can only review proposals from their own sources.")),
            _ => AppResult.Success()
        };
    }

    private async Task UpdateBatchLifecycleAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _reviewBatchRepository.GetByIdAsync(batchId, ct);
        if (batch is null)
            return;

        // Don't touch Canceled or Failed batches
        if (batch.Status is ReviewBatchStatus.Canceled or ReviewBatchStatus.Failed)
            return;

        var proposals = await _reviewProposalRepository.ListByReviewBatchAsync(batchId, ct);

        if (batch.Status == ReviewBatchStatus.Pending)
        {
            // First review transitions to InReview
            await _reviewBatchRepository.UpdateStatusAsync(batchId, ReviewBatchStatus.InReview, ct);
            batch.Status = ReviewBatchStatus.InReview;
        }

        // Check if all proposals are in terminal state
        var allTerminal = proposals.All(p =>
            p.Status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected);

        if (allTerminal && proposals.Count > 0 &&
            batch.Status == ReviewBatchStatus.InReview)
        {
            await _reviewBatchRepository.UpdateCompletedAsync(
                batchId, DateTimeOffset.UtcNow, ct);

            // The last proposal just resolved: if a timeline replay is waiting on this
            // source, it may now advance. Only the extraction batch counts — named sweep
            // batches (Kind != null) are not part of the replay walk. TryAdvance never throws.
            if (batch.Kind is null)
            {
                await _replayAdvancer.TryAdvanceAsync(batch.WorldId, batch.SourceId, ct);
            }
        }
    }
}
