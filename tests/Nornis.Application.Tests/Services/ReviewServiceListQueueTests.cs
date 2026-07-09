using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ReviewServiceListQueueTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private FakeProposalValidator _validator = null!;
    private FakeProposalApplicator _applicator = null!;
    private ReviewService _service = null!;

    // World: "Black Harbor Investigation"
    private Guid _worldId;

    // Users
    private Guid _keldaUserId;   // GM
    private Guid _tavrinUserId;  // Player
    private Guid _jorinUserId;   // Observer

    [SetUp]
    public void SetUp()
    {
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRepo = new InMemorySourceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _unitOfWork = new FakeUnitOfWork();
        _validator = new FakeProposalValidator();
        _applicator = new FakeProposalApplicator();

        _service = new ReviewService(
            _proposalRepo,
            _batchRepo,
            _sourceRepo,
            _artifactRepo,
            _factRepo,
            _relationshipRepo,
            _sourceRefRepo,
            _unitOfWork,
            _validator,
            _applicator);

        _worldId = Guid.NewGuid();
        _keldaUserId = Guid.NewGuid();
        _tavrinUserId = Guid.NewGuid();
        _jorinUserId = Guid.NewGuid();
    }

    #region GM sees all pending proposals regardless of source author

    [Test]
    public async Task ListReviewQueue_GmSeesAllPendingProposals_RegardlessOfSourceAuthor()
    {
        // Source created by Tavrin (Player)
        var tavrinSource = MakeSource(_tavrinUserId, VisibilityScope.PartyVisible, "Tavrin's Session Notes");
        // Source created by Kelda (GM)
        var keldaSource = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Kelda's Notes");

        var batch1 = MakeBatch(tavrinSource.Id);
        var batch2 = MakeBatch(keldaSource.Id);

        var p1 = MakePendingProposal(batch1.Id, "Captain Voss");
        var p2 = MakePendingProposal(batch2.Id, "Silver Key");

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(2));
        Assert.That(result.Value.Proposals.Select(p => p.Id), Contains.Item(p1.Id));
        Assert.That(result.Value.Proposals.Select(p => p.Id), Contains.Item(p2.Id));
    }

    #endregion

    #region Player sees only proposals from own sources

    [Test]
    public async Task ListReviewQueue_PlayerSeesOnlyProposalsFromOwnSources()
    {
        var tavrinSource = MakeSource(_tavrinUserId, VisibilityScope.PartyVisible, "Tavrin's Notes");
        var keldaSource = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Kelda's Notes");

        var batch1 = MakeBatch(tavrinSource.Id);
        var batch2 = MakeBatch(keldaSource.Id);

        var p1 = MakePendingProposal(batch1.Id, "Captain Voss");
        var p2 = MakePendingProposal(batch2.Id, "Silver Key");

        var query = new ReviewQueueQuery(_worldId, _tavrinUserId, WorldRole.Player);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(p1.Id));
    }

    #endregion

    #region Observer gets empty list

    [Test]
    public async Task ListReviewQueue_ObserverGetsEmptyList()
    {
        var tavrinSource = MakeSource(_tavrinUserId, VisibilityScope.PartyVisible, "Tavrin's Notes");
        var batch = MakeBatch(tavrinSource.Id);
        MakePendingProposal(batch.Id, "Captain Voss");

        var query = new ReviewQueueQuery(_worldId, _jorinUserId, WorldRole.Observer);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Is.Empty);
        Assert.That(result.Value.HasMore, Is.False);
    }

    #endregion

    #region GMOnly source proposals hidden from Players

    [Test]
    public async Task ListReviewQueue_GmOnlySourceProposalsHiddenFromPlayers()
    {
        var gmOnlySource = MakeSource(_keldaUserId, VisibilityScope.GMOnly, "GM Secret Notes");
        var batch = MakeBatch(gmOnlySource.Id);
        MakePendingProposal(batch.Id, "Hidden NPC");

        // Player (Tavrin) should not see proposals from GMOnly sources
        var query = new ReviewQueueQuery(_worldId, _tavrinUserId, WorldRole.Player);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Is.Empty);
    }

    [Test]
    public async Task ListReviewQueue_GmOnlySourceProposalsVisibleToGm()
    {
        var gmOnlySource = MakeSource(_keldaUserId, VisibilityScope.GMOnly, "GM Secret Notes");
        var batch = MakeBatch(gmOnlySource.Id);
        var proposal = MakePendingProposal(batch.Id, "Hidden NPC");

        // GM (Kelda) should see proposals from GMOnly sources
        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(proposal.Id));
    }

    #endregion

    #region Private source proposals hidden from non-creator non-GMs

    [Test]
    public async Task ListReviewQueue_PrivateSourceProposalsHiddenFromOtherPlayers()
    {
        // Tavrin creates a Private source — another player shouldn't see its proposals
        var privateSource = MakeSource(_tavrinUserId, VisibilityScope.Private, "Tavrin's Private Journal");
        var batch = MakeBatch(privateSource.Id);
        MakePendingProposal(batch.Id, "Private Secret");

        // A different player can't see Private source proposals
        var otherPlayerId = Guid.NewGuid();
        var query = new ReviewQueueQuery(_worldId, otherPlayerId, WorldRole.Player);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Is.Empty);
    }

    [Test]
    public async Task ListReviewQueue_PrivateSourceProposalsVisibleToCreator()
    {
        var privateSource = MakeSource(_tavrinUserId, VisibilityScope.Private, "Tavrin's Private Journal");
        var batch = MakeBatch(privateSource.Id);
        var proposal = MakePendingProposal(batch.Id, "Private Secret");

        // Tavrin (creator) should see their own private source proposals
        var query = new ReviewQueueQuery(_worldId, _tavrinUserId, WorldRole.Player);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(proposal.Id));
    }

    [Test]
    public async Task ListReviewQueue_PrivateSourceProposalsVisibleToGm()
    {
        var privateSource = MakeSource(_tavrinUserId, VisibilityScope.Private, "Tavrin's Private Journal");
        var batch = MakeBatch(privateSource.Id);
        var proposal = MakePendingProposal(batch.Id, "Private Secret");

        // GM should see Private source proposals
        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(proposal.Id));
    }

    #endregion

    #region PartyVisible proposals visible to authorized reviewers

    [Test]
    public async Task ListReviewQueue_PartyVisibleProposalsVisibleToSourceCreator()
    {
        var partySource = MakeSource(_tavrinUserId, VisibilityScope.PartyVisible, "Session Recap");
        var batch = MakeBatch(partySource.Id);
        var proposal = MakePendingProposal(batch.Id, "Black Harbor");

        var query = new ReviewQueueQuery(_worldId, _tavrinUserId, WorldRole.Player);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(proposal.Id));
    }

    [Test]
    public async Task ListReviewQueue_PartyVisibleProposalsVisibleToGm()
    {
        var partySource = MakeSource(_tavrinUserId, VisibilityScope.PartyVisible, "Session Recap");
        var batch = MakeBatch(partySource.Id);
        var proposal = MakePendingProposal(batch.Id, "Black Harbor");

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(proposal.Id));
    }

    [Test]
    public async Task ListReviewQueue_PartyVisibleProposalsNotVisibleToOtherPlayer()
    {
        // PartyVisible source created by Tavrin — another Player cannot review it
        // because Players only see proposals from their OWN sources
        var partySource = MakeSource(_tavrinUserId, VisibilityScope.PartyVisible, "Session Recap");
        var batch = MakeBatch(partySource.Id);
        MakePendingProposal(batch.Id, "Black Harbor");

        var otherPlayerId = Guid.NewGuid();
        var query = new ReviewQueueQuery(_worldId, otherPlayerId, WorldRole.Player);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Is.Empty);
    }

    #endregion

    #region Ordering: CreatedAt ascending within batches, batches ordered by CreatedAt ascending

    [Test]
    public async Task ListReviewQueue_OrderedByBatchCreatedAtThenProposalCreatedAt()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Multi-Session Notes");

        // Create batches with distinct CreatedAt — batch2 is older
        var batch2 = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        _batchRepo.CreateAsync(batch2).GetAwaiter().GetResult();

        var batch1 = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _batchRepo.CreateAsync(batch1).GetAwaiter().GetResult();

        // Proposals in batch2 (older batch) — p2b is newer than p2a within batch
        var p2a = MakePendingProposalWithCreatedAt(batch2.Id, "Older Batch First",
            DateTimeOffset.UtcNow.AddMinutes(-50));
        var p2b = MakePendingProposalWithCreatedAt(batch2.Id, "Older Batch Second",
            DateTimeOffset.UtcNow.AddMinutes(-40));

        // Proposals in batch1 (newer batch)
        var p1a = MakePendingProposalWithCreatedAt(batch1.Id, "Newer Batch First",
            DateTimeOffset.UtcNow.AddMinutes(-30));
        var p1b = MakePendingProposalWithCreatedAt(batch1.Id, "Newer Batch Second",
            DateTimeOffset.UtcNow.AddMinutes(-20));

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var ids = result.Value!.Proposals.Select(p => p.Id).ToList();
        Assert.That(ids, Has.Count.EqualTo(4));
        // batch2 proposals first (older batch), ordered by CreatedAt within
        Assert.That(ids[0], Is.EqualTo(p2a.Id));
        Assert.That(ids[1], Is.EqualTo(p2b.Id));
        // Then batch1 proposals (newer batch)
        Assert.That(ids[2], Is.EqualTo(p1a.Id));
        Assert.That(ids[3], Is.EqualTo(p1b.Id));
    }

    #endregion

    #region Pagination: max 200 returned, HasMore=true when more exist

    [Test]
    public async Task ListReviewQueue_ReturnsMax200_HasMoreTrueWhenMoreExist()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Big Extraction");
        var batch = MakeBatch(source.Id);

        // Create 201 proposals
        for (var i = 0; i < 201; i++)
        {
            MakePendingProposalWithCreatedAt(batch.Id, $"Proposal {i}",
                DateTimeOffset.UtcNow.AddMinutes(i));
        }

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(200));
        Assert.That(result.Value.HasMore, Is.True);
    }

    [Test]
    public async Task ListReviewQueue_Exactly200_HasMoreFalse()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Exact Batch");
        var batch = MakeBatch(source.Id);

        for (var i = 0; i < 200; i++)
        {
            MakePendingProposalWithCreatedAt(batch.Id, $"Proposal {i}",
                DateTimeOffset.UtcNow.AddMinutes(i));
        }

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(200));
        Assert.That(result.Value.HasMore, Is.False);
    }

    #endregion

    #region FilterByBatchId returns only matching batch proposals

    [Test]
    public async Task ListReviewQueue_FilterByBatchId_ReturnsOnlyMatchingBatchProposals()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Two-Batch Source");
        var batch1 = MakeBatch(source.Id);
        var batch2 = MakeBatch(source.Id);

        var p1 = MakePendingProposal(batch1.Id, "From Batch 1");
        var p2 = MakePendingProposal(batch2.Id, "From Batch 2");

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM, FilterByBatchId: batch1.Id);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Has.Count.EqualTo(1));
        Assert.That(result.Value.Proposals[0].Id, Is.EqualTo(p1.Id));
    }

    #endregion

    #region Non-existent BatchId returns not-found

    [Test]
    public async Task ListReviewQueue_NonExistentBatchId_ReturnsNotFound()
    {
        var nonExistentBatchId = Guid.NewGuid();
        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM, FilterByBatchId: nonExistentBatchId);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task ListReviewQueue_BatchIdFromDifferentWorld_ReturnsNotFound()
    {
        // Batch exists but belongs to a different world
        var otherWorldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = otherWorldId,
            Type = SourceType.SessionNote,
            Title = "Other World Source",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _keldaUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(source);

        var otherWorldBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = otherWorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _batchRepo.CreateAsync(otherWorldBatch).GetAwaiter().GetResult();

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM,
            FilterByBatchId: otherWorldBatch.Id);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    #endregion

    #region Empty queue returns empty list with no error

    [Test]
    public async Task ListReviewQueue_EmptyQueue_ReturnsEmptyListNoError()
    {
        // No sources, batches, or proposals in this world
        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Is.Empty);
        Assert.That(result.Value.HasMore, Is.False);
    }

    [Test]
    public async Task ListReviewQueue_AllProposalsAlreadyReviewed_ReturnsEmptyList()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Reviewed Source");
        var batch = MakeBatch(source.Id);

        // Only Pending proposals appear in the queue — Accepted ones do not
        var acceptedProposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Captain Voss","type":"Character"}""",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Accepted,
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ReviewedByUserId = _keldaUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _proposalRepo.CreateAsync(acceptedProposal).GetAwaiter().GetResult();

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);

        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Proposals, Is.Empty);
    }

    #endregion

    #region Helpers

    #region Proposal display context (source title + target names)

    [Test]
    public async Task ListReviewQueue_ContextCarriesSourceTitleAndTargetNames()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Session 13: The Vault Job");
        var batch = MakeBatch(source.Id);

        var voss = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(voss);

        // AddFact targeting an existing artifact by id
        var addFact = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.AddFact,
            TargetType = ReviewTargetType.ArtifactFact,
            TargetId = voss.Id,
            ProposedValueJson = """{"predicate":"rank","value":"Captain"}""",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        _proposalRepo.CreateAsync(addFact).GetAwaiter().GetResult();

        // AddRelationship referencing one id and one same-batch name
        var addRel = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.AddRelationship,
            TargetType = ReviewTargetType.ArtifactRelationship,
            ProposedValueJson = $$"""{"artifactAId":"{{voss.Id}}","artifactBName":"Black Harbor","type":"LocatedIn"}""",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-9)
        };
        _proposalRepo.CreateAsync(addRel).GetAwaiter().GetResult();

        // CreateArtifact needs no target name — its payload carries the name
        var create = MakePendingProposal(batch.Id, "Silver Key");

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);
        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var context = result.Value!.Context;
        Assert.That(context, Is.Not.Null);

        Assert.That(context![addFact.Id].SourceTitle, Is.EqualTo("Session 13: The Vault Job"));
        Assert.That(context[addFact.Id].SourceId, Is.EqualTo(source.Id));
        Assert.That(context[addFact.Id].TargetName, Is.EqualTo("Captain Voss"));

        Assert.That(context[addRel.Id].TargetName, Is.EqualTo("Captain Voss ↔ Black Harbor"));

        Assert.That(context[create.Id].TargetName, Is.Null);
        Assert.That(context[create.Id].SourceTitle, Is.EqualTo("Session 13: The Vault Job"));
    }

    [Test]
    public async Task ListReviewQueue_MergeProposal_ContextNamesBothArtifacts()
    {
        var source = MakeSource(_keldaUserId, VisibilityScope.PartyVisible, "Kelda's Notes");
        var batch = MakeBatch(source.Id);

        Artifact MakeArtifact(string name)
        {
            var a = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = _worldId,
                Type = ArtifactType.Storyline,
                Name = name,
                Status = ArtifactStatus.Active,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _artifactRepo.Seed(a);
            return a;
        }

        var keep = MakeArtifact("The Missing Caravan");
        var dupe = MakeArtifact("The Missing Caravan (duplicate)");

        var merge = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.MergeArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = keep.Id,
            ProposedValueJson = $$"""{"sourceArtifactId":"{{dupe.Id}}"}""",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        _proposalRepo.CreateAsync(merge).GetAwaiter().GetResult();

        var query = new ReviewQueueQuery(_worldId, _keldaUserId, WorldRole.GM);
        var result = await _service.ListReviewQueueAsync(query, CancellationToken.None);

        var context = result.Value!.Context!;
        Assert.That(context[merge.Id].TargetName, Is.EqualTo("The Missing Caravan"));
        Assert.That(context[merge.Id].MergeSourceName, Is.EqualTo("The Missing Caravan (duplicate)"));
    }

    #endregion

    private Source MakeSource(Guid createdByUserId, VisibilityScope visibility, string title)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = title,
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private ReviewBatch MakeBatch(Guid sourceId)
    {
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(batch).GetAwaiter().GetResult();
        return batch;
    }

    private ReviewProposal MakePendingProposal(Guid batchId, string artifactName)
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = $$$"""{"name":"{{{artifactName}}}","type":"Character"}""",
            Rationale = $"Extracted {artifactName} from session notes.",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15)
        };
        _proposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        return proposal;
    }

    private ReviewProposal MakePendingProposalWithCreatedAt(
        Guid batchId, string artifactName, DateTimeOffset createdAt)
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = $$$"""{"name":"{{{artifactName}}}","type":"Character"}""",
            Rationale = $"Extracted {artifactName} from session notes.",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = createdAt
        };
        _proposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        return proposal;
    }

    #endregion
}
