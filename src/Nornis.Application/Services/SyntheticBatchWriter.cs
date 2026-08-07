using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// The provenance a synthetic batch must carry: which world, who acted, and the title and
/// body of the source that will record the act. The writer stamps everything else — a
/// synthetic source is always born Processed, because it records something that already
/// happened and must never be picked up for extraction.
/// </summary>
public sealed record SyntheticSourceSpec
{
    public required Guid WorldId { get; init; }
    public required Guid ActingUserId { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public SourceType Type { get; init; } = SourceType.GMNote;
    public VisibilityScope Visibility { get; init; } = VisibilityScope.GMOnly;

    /// <summary>Reveal only: the GM's words to the party, kept out of the composed body.</summary>
    public string? RevealNote { get; init; }
}

/// <summary>
/// One reviewable change in a synthetic batch. ReferenceNotes and ReferenceQuote apply
/// only to pending batches — a for-review proposal always gets a SourceReference row so
/// the review UI can show where it came from, while an accepted batch records none: the
/// proposal itself, applied and accept-stamped, is the record of the GM's act.
/// </summary>
public sealed record SyntheticProposalSpec
{
    public required ReviewChangeType ChangeType { get; init; }
    public required ReviewTargetType TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public required string ProposedValueJson { get; init; }
    public required string Rationale { get; init; }
    public decimal? Confidence { get; init; }
    public string? ReferenceNotes { get; init; }
    public string? ReferenceQuote { get; init; }
}

public sealed record SyntheticBatchWritten(Guid BatchId, Guid SourceId);

/// <summary>
/// The one writer for synthetic review batches — the provenance invariant (a source
/// recording the act, a batch naming its producer via <see cref="ReviewBatchKinds"/>,
/// proposals carrying the change) was hand-assembled in six services before this, and the
/// hand assembly is how the retrospective shipped with a null Kind and the reveal with an
/// unshared string literal. Two shapes exist and each gets a verb: accepted batches
/// (confirm-and-apply GM operations — wrap-up, merge, reveal) and pending batches
/// (AI drafts awaiting review — continuity fix, retrospective). Sweeps over a real
/// source (relationship backfill) get a third verb because their empty case still writes:
/// a Completed batch whose only meaning is "this source was swept".
///
/// ExtractionService's three batch constructions are deliberately not clients. An
/// extraction batch is the one kind that is one-per-source, its null Kind is what the
/// filtered unique index keys on, and it is written through the conditional-insert verb
/// (TryCreateExtractionBatchAsync) precisely so two workers cannot both commit one.
/// Folding it in here would trade that enforced invariant for surface uniformity.
///
/// Injected concrete, without an interface, on purpose: this class exists to be the one
/// place the invariant lives, and a test that faked it away would be faking away the
/// thing under test.
/// </summary>
public class SyntheticBatchWriter
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IProposalApplicator _proposalApplicator;
    private readonly IArtifactSummaryRefreshQueue _summaryRefreshQueue;
    private readonly IUnitOfWork _unitOfWork;

    public SyntheticBatchWriter(
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IProposalApplicator proposalApplicator,
        IArtifactSummaryRefreshQueue summaryRefreshQueue,
        IUnitOfWork unitOfWork)
    {
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _proposalApplicator = proposalApplicator;
        _summaryRefreshQueue = summaryRefreshQueue;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Confirm-and-apply: mints the synthetic source and a Completed batch, then applies
    /// each proposal through the real applicator and accept-stamps it — all in one
    /// transaction. The first apply failure rolls the whole write back and returns the
    /// applicator's error; the caller's world was not touched.
    /// </summary>
    public async Task<AppResult<SyntheticBatchWritten>> WriteAcceptedAsync(
        SyntheticSourceSpec sourceSpec,
        string kind,
        IReadOnlyList<SyntheticProposalSpec> proposals,
        CancellationToken ct)
    {
        RequireKind(kind);
        RequireProposals(proposals);

        var now = DateTimeOffset.UtcNow;
        var source = BuildSource(sourceSpec, now);
        var batch = BuildBatch(source, kind, ReviewBatchStatus.Completed, now, completedAt: now);

        // Coalesced across the batch's proposals, then requested once after the commit —
        // the same shape the review queue's accept uses, and for the same reason: a merge
        // that folds three artifacts into one should ask for one refresh apiece, not one
        // per proposal, and should ask for none at all if the write rolls back.
        var summaryCandidates = new HashSet<Guid>();
        var summaryPinned = new HashSet<Guid>();

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _sourceRepository.CreateAsync(source, ct);
            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var spec in proposals)
            {
                var proposal = BuildProposal(batch, spec, now);
                await _reviewProposalRepository.CreateAsync(proposal, ct);

                // Every caller of the accepted shape is GM-gated before it builds a spec,
                // so resolution is unrestricted.
                var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, source, VisibilityFilter.All, ct);
                if (!applyResult.IsSuccess)
                {
                    await transaction.RollbackAsync(ct);
                    return AppResult<SyntheticBatchWritten>.Fail(applyResult.Error!);
                }

                summaryCandidates.UnionWith(applyResult.Value!.SummaryRefreshCandidates);
                summaryPinned.UnionWith(applyResult.Value.SummaryPinnedArtifactIds);

                proposal.Accept(sourceSpec.ActingUserId, now);
                await _reviewProposalRepository.UpdateAsync(proposal, ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        // Outside the transaction: the queue is best-effort and must not be able to fail a
        // write that already committed. The applicator reports these on every arm, but until
        // now only the review queue's accept read them — so a GM merging, revealing or
        // wrapping up changed an artifact's content and left its summary describing the
        // world before the change, with nothing to correct it until some later accept
        // happened to touch the same artifact through the queue.
        var refresh = summaryCandidates.Except(summaryPinned).ToList();
        if (refresh.Count > 0)
        {
            await _summaryRefreshQueue.TryEnqueueAsync(sourceSpec.WorldId, refresh, ct);
        }

        return AppResult<SyntheticBatchWritten>.Success(new SyntheticBatchWritten(batch.Id, source.Id));
    }

    /// <summary>
    /// For-review: mints the synthetic source and a Pending batch, one Pending proposal
    /// per spec, and one SourceReference per proposal — all in one transaction. Nothing
    /// is applied; the GM decides in the review queue.
    /// </summary>
    public async Task<SyntheticBatchWritten> WritePendingAsync(
        SyntheticSourceSpec sourceSpec,
        string kind,
        IReadOnlyList<SyntheticProposalSpec> proposals,
        CancellationToken ct)
    {
        RequireKind(kind);
        RequireProposals(proposals);

        var now = DateTimeOffset.UtcNow;
        var source = BuildSource(sourceSpec, now);
        var batch = BuildBatch(source, kind, ReviewBatchStatus.Pending, now, completedAt: null);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _sourceRepository.CreateAsync(source, ct);
            await CreatePendingBatchAsync(batch, source, proposals, now, ct);
            await transaction.CommitAsync(ct);
            return new SyntheticBatchWritten(batch.Id, source.Id);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// A sweep over a real, existing source. Zero proposals still writes: a Completed
    /// batch is the durable mark that this source was swept and found clean, and the
    /// batch's kind is the sweep's per-source idempotency key. With proposals it is the
    /// pending shape over the swept source instead of a synthetic one.
    /// </summary>
    public async Task<Guid> WriteSweepAsync(
        Source source,
        string kind,
        IReadOnlyList<SyntheticProposalSpec> proposals,
        CancellationToken ct)
    {
        RequireKind(kind);

        var now = DateTimeOffset.UtcNow;
        var empty = proposals.Count == 0;
        var batch = BuildBatch(source, kind,
            empty ? ReviewBatchStatus.Completed : ReviewBatchStatus.Pending,
            now, completedAt: empty ? now : null);

        if (empty)
        {
            // A single insert needs no transaction.
            await _reviewBatchRepository.CreateAsync(batch, ct);
            return batch.Id;
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await CreatePendingBatchAsync(batch, source, proposals, now, ct);
            await transaction.CommitAsync(ct);
            return batch.Id;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task CreatePendingBatchAsync(
        ReviewBatch batch, Source source, IReadOnlyList<SyntheticProposalSpec> proposals,
        DateTimeOffset now, CancellationToken ct)
    {
        await _reviewBatchRepository.CreateAsync(batch, ct);

        foreach (var spec in proposals)
        {
            var proposal = BuildProposal(batch, spec, now);
            await _reviewProposalRepository.CreateAsync(proposal, ct);

            await _sourceReferenceRepository.CreateAsync(new SourceReference
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                TargetType = SourceReferenceTargetType.ReviewProposal,
                TargetId = proposal.Id,
                Notes = spec.ReferenceNotes,
                Quote = spec.ReferenceQuote,
                CreatedAt = now
            }, ct);
        }
    }

    private static Source BuildSource(SyntheticSourceSpec spec, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = spec.WorldId,
        Type = spec.Type,
        Title = spec.Title,
        Body = spec.Body,
        RevealNote = spec.RevealNote,
        Visibility = spec.Visibility,
        ProcessingStatus = SourceProcessingStatus.Processed,
        CreatedAt = now,
        CreatedByUserId = spec.ActingUserId
    };

    private static ReviewBatch BuildBatch(
        Source source, string kind, ReviewBatchStatus status, DateTimeOffset now, DateTimeOffset? completedAt) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = source.WorldId,
            SourceId = source.Id,
            Kind = kind,
            Status = status,
            CreatedAt = now,
            CompletedAt = completedAt
        };

    private static ReviewProposal BuildProposal(ReviewBatch batch, SyntheticProposalSpec spec, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ReviewBatchId = batch.Id,
        ChangeType = spec.ChangeType,
        TargetType = spec.TargetType,
        TargetId = spec.TargetId,
        ProposedValueJson = spec.ProposedValueJson,
        Rationale = spec.Rationale,
        Confidence = spec.Confidence,
        Status = ReviewProposalStatus.Pending,
        CreatedAt = now
    };

    private static void RequireKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException(
                "A synthetic batch must name its kind — null Kind is reserved for a source's own extraction batch.",
                nameof(kind));
        }
    }

    private static void RequireProposals(IReadOnlyList<SyntheticProposalSpec> proposals)
    {
        if (proposals.Count == 0)
        {
            throw new ArgumentException(
                "A synthetic batch with no proposals records nothing — callers with nothing to write don't write.",
                nameof(proposals));
        }
    }
}
