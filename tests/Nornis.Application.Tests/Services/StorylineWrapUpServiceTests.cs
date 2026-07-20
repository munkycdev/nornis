using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class StorylineWrapUpServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private FakeProposalApplicator _applicator = null!;
    private FakeReviewService _reviewService = null!;
    private ArtifactService _artifactService = null!;

    private Guid _worldId;
    private Guid _gmUserId;

    private static readonly DateTimeOffset Base = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _applicator = new FakeProposalApplicator();
        _reviewService = new FakeReviewService();
        _artifactService = new ArtifactService(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo,
            _sourceRepo, new InMemoryCharacterRepository(), new InMemoryWorldMemberRepository(),
            new InMemoryStorylineCampaignRepository(), new InMemoryCampaignRepository());

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private StorylineWrapUpService Service(int staleThreshold = 2, int recentWindow = 2)
    {
        var reader = new StorylineDevelopmentReader(
            _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo);
        return new StorylineWrapUpService(
            reader,
            Options.Create(new ContinuityOptions { StaleThresholdSessions = staleThreshold, RecentSessionWindow = recentWindow }),
            _artifactRepo,
            _proposalRepo,
            _batchRepo,
            _sourceRepo,
            _applicator,
            _reviewService,
            _artifactService,
            new FakeUnitOfWork(),
            NullLogger<StorylineWrapUpService>.Instance);
    }

    private Artifact SeedStoryline(string name, ArtifactStatus status = ArtifactStatus.Active)
    {
        var storyline = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Storyline,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = status,
            CreatedAt = Base,
            UpdatedAt = Base
        };
        _artifactRepo.Seed(storyline);
        return storyline;
    }

    private Source SeedSession(int dayOffset)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = $"Session +{dayOffset}",
            Body = "…",
            Visibility = VisibilityScope.PartyVisible,
            OccurredAt = Base.AddDays(dayOffset),
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = Base.AddDays(dayOffset),
            CreatedByUserId = _gmUserId
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private void SeedDevelopment(Artifact storyline, Source session)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = storyline.Id,
            Predicate = "development",
            Value = $"beat at {session.Title}",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = session.OccurredAt!.Value,
            UpdatedAt = session.OccurredAt!.Value
        };
        _factRepo.Seed(fact);
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = session.Id,
            TargetType = SourceReferenceTargetType.ArtifactFact,
            TargetId = fact.Id,
            CreatedAt = session.OccurredAt!.Value
        });
    }

    private ReviewProposal SeedPendingPartOf(Artifact child, Artifact parent)
    {
        var payload = JsonSerializer.Serialize(new
        {
            artifactAId = child.Id,
            artifactBId = parent.Id,
            type = ArtifactService.PartOfRelationshipType,
            truthState = "Confirmed"
        });
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = Guid.NewGuid(),
            ChangeType = ReviewChangeType.AddRelationship,
            TargetType = ReviewTargetType.ArtifactRelationship,
            ProposedValueJson = payload,
            Rationale = $"{child.Name} opened inside {parent.Name}.",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = Base
        };
        _proposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        return proposal;
    }

    // ------------------------------------------------------------------ Role gates --

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task GetWrapUp_NonGm_Returns403(WorldRole role)
    {
        var result = await Service().GetWrapUpAsync(_worldId, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Apply_NonGm_Returns403()
    {
        var command = new WrapUpDecisionsCommand(_worldId, Guid.NewGuid(), WorldRole.Player, [], [], [], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    // --------------------------------------------------------------------- Read --

    [Test]
    public async Task GetWrapUp_EmptyWorld_HasNoWork()
    {
        var result = await Service().GetWrapUpAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.HasWork, Is.False);
        Assert.That(result.Value.GoneQuiet, Is.Empty);
        Assert.That(result.Value.CouldNest, Is.Empty);
        Assert.That(result.Value.UnparentedArcs, Is.Empty);
    }

    [Test]
    public async Task GetWrapUp_SurfacesQuietNestAndUnparented()
    {
        // 4 sessions; window = 2 (recent = days 20 and 30).
        var oldArc = SeedStoryline("Old Arc");
        var busy = SeedStoryline("Busy Arc");
        var newArc = SeedStoryline("New Arc");

        SeedDevelopment(oldArc, SeedSession(1));   // last touched day 1 → quiet
        SeedDevelopment(busy, SeedSession(10));
        SeedDevelopment(busy, SeedSession(20));
        SeedDevelopment(newArc, SeedSession(30));  // first & only dev in recent window → unparented arc

        var nestProposal = SeedPendingPartOf(newArc, busy); // child recently touched → could nest

        var result = await Service(staleThreshold: 2, recentWindow: 2)
            .GetWrapUpAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        var view = result.Value!;
        Assert.That(view.HasWork, Is.True);
        Assert.That(view.GoneQuiet.Select(q => q.Name), Does.Contain("Old Arc"));
        Assert.That(view.UnparentedArcs.Select(u => u.Name), Does.Contain("New Arc"));
        var nest = view.CouldNest.Single();
        Assert.That(nest.ProposalId, Is.EqualTo(nestProposal.Id));
        Assert.That(nest.ChildName, Is.EqualTo("New Arc"));
        Assert.That(nest.ParentName, Is.EqualTo("Busy Arc"));
        Assert.That(view.ParentOptions, Has.Count.EqualTo(3));
        Assert.That(view.Advanced.Select(a => a.Name), Is.EquivalentTo(new[] { "Busy Arc", "New Arc" }));
    }

    [Test]
    public async Task GetWrapUp_CouldNest_ExcludesChildNotRecentlyTouched()
    {
        var oldChild = SeedStoryline("Old Child");
        var parent = SeedStoryline("Parent");
        SeedDevelopment(oldChild, SeedSession(1));         // child touched long ago
        SeedDevelopment(parent, SeedSession(20));
        SeedDevelopment(parent, SeedSession(30));
        SeedPendingPartOf(oldChild, parent);

        var result = await Service(staleThreshold: 2, recentWindow: 2)
            .GetWrapUpAsync(_worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.CouldNest, Is.Empty);
    }

    // --------------------------------------------------------------------- Apply --

    [Test]
    public async Task Apply_InvalidClosureStatus_Returns400()
    {
        var arc = SeedStoryline("Arc");
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [new WrapUpClosure(arc.Id, ArtifactStatus.Active)], [], [], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Apply_Closure_CreatesWrapUpBatchAndAcceptsProposal()
    {
        var arc = SeedStoryline("Finished Arc");
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [new WrapUpClosure(arc.Id, ArtifactStatus.Resolved)], [], [], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Closed, Is.EqualTo(1));
        Assert.That(result.Value.BatchId, Is.Not.Null);

        var batch = _batchRepo.Batches.Single();
        Assert.That(batch.Kind, Is.EqualTo(StorylineWrapUpService.BatchKind));

        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.UpdateArtifact));
        Assert.That(proposal.TargetId, Is.EqualTo(arc.Id));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(_gmUserId));
        Assert.That(proposal.ProposedValueJson, Does.Contain("Resolved"));
    }

    [Test]
    public async Task Apply_Closure_UnknownStoryline_Returns404()
    {
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [new WrapUpClosure(Guid.NewGuid(), ArtifactStatus.Dormant)], [], [], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Apply_Closure_ApplicatorFailure_Propagates()
    {
        var arc = SeedStoryline("Arc");
        _applicator.ConfigureFailure("boom", "no good");
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [new WrapUpClosure(arc.Id, ArtifactStatus.Dormant)], [], [], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("boom"));
    }

    [Test]
    public async Task Apply_Parent_CreatesPartOfViaArtifactService()
    {
        var child = SeedStoryline("Child");
        var parent = SeedStoryline("Parent");
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [], [], [], [new WrapUpParentAssignment(child.Id, parent.Id)]);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Parented, Is.EqualTo(1));
        var link = _relationshipRepo.Relationships.Single();
        Assert.That(link.Type, Is.EqualTo(ArtifactService.PartOfRelationshipType));
        Assert.That(link.ArtifactAId, Is.EqualTo(child.Id));
        Assert.That(link.ArtifactBId, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task Apply_AcceptAndReject_DelegateToReviewService()
    {
        var acceptId = Guid.NewGuid();
        var rejectId = Guid.NewGuid();
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [], [acceptId], [rejectId], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Nested, Is.EqualTo(1));
        Assert.That(result.Value.Rejected, Is.EqualTo(1));
        Assert.That(_reviewService.Accepted, Does.Contain(acceptId));
        Assert.That(_reviewService.Rejected, Does.Contain(rejectId));
    }

    [Test]
    public async Task Apply_ReviewServiceError_Propagates()
    {
        _reviewService.FailAccept = new AppError(409, "conflict", "already rejected");
        var command = new WrapUpDecisionsCommand(_worldId, _gmUserId, WorldRole.GM,
            [], [Guid.NewGuid()], [], []);

        var result = await Service().ApplyAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
    }

    // A minimal IReviewService double: only accept/reject are exercised by the wrap-up.
    private sealed class FakeReviewService : IReviewService
    {
        public List<Guid> Accepted { get; } = [];
        public List<Guid> Rejected { get; } = [];
        public AppError? FailAccept { get; set; }
        public AppError? FailReject { get; set; }

        public Task<AppResult<AcceptProposalResult>> AcceptProposalAsync(AcceptProposalCommand command, CancellationToken ct)
        {
            if (FailAccept is not null)
                return Task.FromResult(AppResult<AcceptProposalResult>.Fail(FailAccept));
            Accepted.Add(command.ProposalId);
            return Task.FromResult(AppResult<AcceptProposalResult>.Success(
                new AcceptProposalResult(command.ProposalId, ReviewProposalStatus.Accepted, DateTimeOffset.UtcNow, command.ActingUserId, null)));
        }

        public Task<AppResult<RejectProposalResult>> RejectProposalAsync(RejectProposalCommand command, CancellationToken ct)
        {
            if (FailReject is not null)
                return Task.FromResult(AppResult<RejectProposalResult>.Fail(FailReject));
            Rejected.Add(command.ProposalId);
            return Task.FromResult(AppResult<RejectProposalResult>.Success(
                new RejectProposalResult(command.ProposalId, ReviewProposalStatus.Rejected, DateTimeOffset.UtcNow, command.ActingUserId)));
        }

        public Task<AppResult<ReviewQueueResult>> ListReviewQueueAsync(ReviewQueueQuery query, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AppResult<EditProposalResult>> EditProposalAsync(EditProposalCommand command, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AppResult<BatchOperationResult>> BatchAcceptAsync(BatchAcceptCommand command, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AppResult<BatchOperationResult>> BatchRejectAsync(BatchRejectCommand command, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
