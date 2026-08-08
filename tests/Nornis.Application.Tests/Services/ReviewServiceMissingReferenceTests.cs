using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Single accept, when the proposal names an artifact nothing resolves. The reviewer used to
/// get "does not exist and is not proposed in this batch" and a payload to hand-edit; now the
/// accept works down a ladder — the batch's own Create for the name, then the artifacts that
/// resemble it, then creating it.
///
/// The rung that matters most is the middle one, and it matters because it STOPS. Deciding
/// that "Voss" and "Captain Voss" are one thing is the GM's call, per
/// <see cref="Nornis.Domain.Models.ArtifactNameKey"/>; the pipeline only ever creates a name
/// nothing in the world resembles.
/// </summary>
[TestFixture]
public class ReviewServiceMissingReferenceTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private ReviewService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;

    [SetUp]
    public void SetUp()
    {
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRepo = new InMemorySourceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();

        // Real validator and applicator: the ladder is only meaningful if name resolution
        // really runs and really fails.
        var applicator = new ProposalApplicator(
            _artifactRepo, _factRepo, relationshipRepo, sourceRefRepo,
            new InMemorySourceAttachmentRepository(), new InMemoryMapPlacemarkRepository(),
            new InMemoryWorldMemberRepository());

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 4: The lighthouse",
            Body = "Something haunts the lighthouse.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();

        _service = new ReviewService(
            _proposalRepo, _batchRepo, _sourceRepo, _artifactRepo, _factRepo,
            relationshipRepo, sourceRefRepo, new FakeUnitOfWork(),
            new ProposalValidator(), applicator,
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance,
            summaryRefreshQueue: NoOpArtifactSummaryRefreshQueue.Instance);
    }

    #region Nothing resembles the name: create it

    [Test]
    public async Task UnknownName_IsCreatedAndTheFactAttachesToIt()
    {
        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);

        var artifact = _artifactRepo.Artifacts.Single();
        Assert.That(artifact.Name, Is.EqualTo("Ghost"));
        Assert.That(artifact.Type, Is.EqualTo(ArtifactType.Concept));
        Assert.That(_factRepo.Facts.Single().ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task UnknownName_IsReportedBackSoTheReviewerHearsCanonGrew()
    {
        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact);

        Assert.That(result.Value!.CreatedMissingArtifactNames, Is.EqualTo(["Ghost"]));
    }

    [Test]
    public async Task OrdinaryAccept_ReportsNothingCreated()
    {
        _artifactRepo.Seed(MakeArtifact("Ghost", ArtifactType.Concept));
        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact);

        Assert.That(result.Value!.CreatedMissingArtifactNames, Is.Null);
    }

    [Test]
    public async Task TheCreatedArtifactLandsAsAnAcceptedProposal_NotBehindTheRecordsBack()
    {
        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        await AcceptAsync(fact);

        var added = (await _proposalRepo.ListByReviewBatchAsync(_batch.Id))
            .Single(p => p.ChangeType == ReviewChangeType.CreateArtifact);

        Assert.That(added.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(added.ReviewedByUserId, Is.EqualTo(_gmUserId));
        Assert.That(added.Rationale, Does.Contain("Ghost"));
    }

    [Test]
    public async Task OnlyTheUnresolvedEndOfARelationshipIsCreated()
    {
        _artifactRepo.Seed(MakeArtifact("Black Harbor", ArtifactType.Location));
        var relationship = await SeedAsync(MakeAddRelationshipByName("Ghost", "Black Harbor"));

        var result = await AcceptAsync(relationship);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.CreatedMissingArtifactNames, Is.EqualTo(["Ghost"]),
            "the end that already existed must not be duplicated");
        Assert.That(_artifactRepo.Artifacts.Select(a => a.Name),
            Is.EquivalentTo(["Black Harbor", "Ghost"]));
    }

    #endregion

    #region Something resembles the name: ask

    [Test]
    public async Task NameCanonAlmostHas_RefusesAndNamesTheCandidate()
    {
        _artifactRepo.Seed(MakeArtifact("Captain Voss", ArtifactType.Character));
        var fact = await SeedAsync(MakeAddFactByName("Voss", "commands", "the harbor guard"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_near_match"));
        Assert.That(result.Error.Message, Does.Contain("Captain Voss"));
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1), "no twin created behind the question");
        Assert.That(fact.Status, Is.EqualTo(ReviewProposalStatus.Pending));
    }

    [Test]
    public async Task TheFullerNameAlsoFindsTheShorterOne()
    {
        // Extraction writes the fuller name at least as often as the shorter one, and a
        // one-directional score only catches the shorter case.
        _artifactRepo.Seed(MakeArtifact("Voss", ArtifactType.Character));
        var fact = await SeedAsync(MakeAddFactByName("Captain Voss", "commands", "the harbor guard"));

        var result = await AcceptAsync(fact);

        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_near_match"));
        Assert.That(result.Error.Message, Does.Contain("Voss"));
    }

    [Test]
    public async Task CreateMissingArtifact_IsTheReviewersAnswerAndCreatesItAnyway()
    {
        _artifactRepo.Seed(MakeArtifact("Captain Voss", ArtifactType.Character));
        var fact = await SeedAsync(MakeAddFactByName("Voss", "commands", "the harbor guard"));

        var result = await AcceptAsync(fact, createMissing: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(_artifactRepo.Artifacts.Select(a => a.Name),
            Is.EquivalentTo(["Captain Voss", "Voss"]));
    }

    [Test]
    public async Task ASharedPrefixIsWorthAGlance()
    {
        _artifactRepo.Seed(MakeArtifact("Vossberg", ArtifactType.Location));
        var fact = await SeedAsync(MakeAddFactByName("Voss", "commands", "the harbor guard"));

        var result = await AcceptAsync(fact);

        Assert.That(result.Error!.Code, Is.EqualTo("artifact_name_near_match"));
    }

    [Test]
    public async Task ASubstringBuriedInALongerNameIsNot()
    {
        // "Voss" inside "Elvossir" is a coincidence, not a near miss. Asking about it would
        // suppress the create for a name that genuinely is new.
        _artifactRepo.Seed(MakeArtifact("Elvossir", ArtifactType.Location));
        var fact = await SeedAsync(MakeAddFactByName("Voss", "commands", "the harbor guard"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.CreatedMissingArtifactNames, Is.EqualTo(["Voss"]));
    }

    [Test]
    public async Task ArchivedMergeLeftoversAreNotOfferedAsCandidates()
    {
        var archived = MakeArtifact("Captain Voss", ArtifactType.Character);
        archived.Status = ArtifactStatus.Archived;
        _artifactRepo.Seed(archived);

        var fact = await SeedAsync(MakeAddFactByName("Voss", "commands", "the harbor guard"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.CreatedMissingArtifactNames, Is.EqualTo(["Voss"]));
    }

    #endregion

    #region The reviewer already said no

    [Test]
    public async Task RejectedSiblingCreate_IsNotPutBackByTheSideDoor()
    {
        var rejectedCreate = await SeedAsync(MakeCreate("Ghost", "Concept"));
        rejectedCreate.Reject(_gmUserId, DateTimeOffset.UtcNow);

        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("artifact_create_rejected"));
        Assert.That(_artifactRepo.Artifacts, Is.Empty);
    }

    [Test]
    public async Task RejectedSiblingCreate_YieldsToAnExplicitCreateMissing()
    {
        // Changing your mind out loud is allowed; it is the silent resurrection that is not.
        var rejectedCreate = await SeedAsync(MakeCreate("Ghost", "Concept"));
        rejectedCreate.Reject(_gmUserId, DateTimeOffset.UtcNow);

        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact, createMissing: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(_artifactRepo.Artifacts.Single().Name, Is.EqualTo("Ghost"));
    }

    #endregion

    #region Existing rungs still work

    [Test]
    public async Task PendingSiblingCreate_IsStillPreferredOverCreatingAnything()
    {
        var create = await SeedAsync(MakeCreate("Ghost", "Character"));
        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(create.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(_artifactRepo.Artifacts.Single().Type, Is.EqualTo(ArtifactType.Character),
            "the batch's own Create carries a real type; the fallback's Concept must not win");
        Assert.That(result.Value!.CreatedMissingArtifactNames, Is.Null);
    }

    [Test]
    public async Task PascalCasePayloads_AreReadTheSameWayEveryOtherReaderReadsThem()
    {
        // The applicator and validator deserialize case-insensitively, so this payload applies
        // perfectly well. The ladder used to be blind to it and would create a twin of an
        // artifact its own batch was already creating.
        var create = await SeedAsync(MakeProposal(
            ReviewChangeType.CreateArtifact, ReviewTargetType.Artifact,
            JsonSerializer.Serialize(new CreateArtifactPayload("Ghost", "Character", null, "PartyVisible", 0.8m))));
        var fact = await SeedAsync(MakeAddFactByName("Ghost", "haunts", "the lighthouse"));

        var result = await AcceptAsync(fact);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(create.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(_artifactRepo.Artifacts, Has.Count.EqualTo(1));
    }

    #endregion

    private Task<Nornis.Application.Errors.AppResult<AcceptProposalResult>> AcceptAsync(
        ReviewProposal proposal, bool createMissing = false) =>
        _service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM, createMissing),
            CancellationToken.None);

    private async Task<ReviewProposal> SeedAsync(ReviewProposal proposal)
    {
        await _proposalRepo.CreateAsync(proposal);
        return proposal;
    }

    private Artifact MakeArtifact(string name, ArtifactType type) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };

    private ReviewProposal MakeCreate(string name, string type) =>
        MakeProposal(ReviewChangeType.CreateArtifact, ReviewTargetType.Artifact,
            $$"""{"name":"{{name}}","type":"{{type}}","visibility":"PartyVisible","confidence":0.8}""");

    private ReviewProposal MakeAddFactByName(string artifactName, string predicate, string value) =>
        MakeProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact,
            $$"""{"artifactName":"{{artifactName}}","predicate":"{{predicate}}","value":"{{value}}","confidence":0.8}""");

    private ReviewProposal MakeAddRelationshipByName(string a, string b) =>
        MakeProposal(ReviewChangeType.AddRelationship, ReviewTargetType.ArtifactRelationship,
            $$"""{"artifactAName":"{{a}}","artifactBName":"{{b}}","type":"LocatedIn","confidence":0.8}""");

    private ReviewProposal MakeProposal(ReviewChangeType changeType, ReviewTargetType targetType, string json) =>
        new()
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = changeType,
            TargetType = targetType,
            ProposedValueJson = json,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
}
