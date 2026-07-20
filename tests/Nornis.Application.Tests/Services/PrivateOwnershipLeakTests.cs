using Microsoft.Extensions.Options;
using Nornis.Application.Application;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The leak matrix for Private knowledge: for every player-facing read surface, player A's
/// Private artifact/fact/relationship must be visible to A and the GM, and invisible to
/// player B. These tests exist because the pre-ownership implementation leaked all Private
/// content to every player.
/// </summary>
[TestFixture]
[Category("Feature: content-visibility")]
public class PrivateOwnershipLeakTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid PlayerA = Guid.NewGuid();
    private static readonly Guid PlayerB = Guid.NewGuid();
    private static readonly Guid GmUser = Guid.NewGuid();

    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;

    private Artifact _privateArtifact = null!;   // A's private artifact
    private Artifact _partyArtifact = null!;     // shared artifact everyone sees
    private ArtifactFact _privateFact = null!;   // A's private fact on the shared artifact
    private ArtifactRelationship _privateRelationship = null!; // A's private edge between the two

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();

        var now = DateTimeOffset.UtcNow;
        _privateArtifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Character,
            Name = "Secret Contact", Visibility = VisibilityScope.Private,
            CreatedByUserId = PlayerA, Status = ArtifactStatus.Active,
            CreatedAt = now, UpdatedAt = now
        };
        _partyArtifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Location,
            Name = "Black Harbor", Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        _privateFact = new ArtifactFact
        {
            Id = Guid.NewGuid(), ArtifactId = _partyArtifact.Id,
            Predicate = "secret entrance", Value = "behind the fish market",
            TruthState = TruthState.Likely, Visibility = VisibilityScope.Private,
            CreatedByUserId = PlayerA, CreatedAt = now, UpdatedAt = now
        };
        _privateRelationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(), WorldId = WorldId,
            ArtifactAId = _privateArtifact.Id, ArtifactBId = _partyArtifact.Id,
            Type = "HidesIn", TruthState = TruthState.Likely,
            Visibility = VisibilityScope.Private, CreatedByUserId = PlayerA,
            CreatedAt = now, UpdatedAt = now
        };

        _artifactRepo.Seed(_privateArtifact, _partyArtifact);
        _factRepo.Seed(_privateFact);
        _relationshipRepo.Seed(_privateRelationship);
    }

    private ArtifactService CreateArtifactService() => new(
        _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo,
        new InMemoryCharacterRepository(), new InMemoryWorldMemberRepository(),
        new InMemoryStorylineCampaignRepository(), new InMemoryCampaignRepository());

    private KeywordKnowledgeRetriever CreateRetriever() => new(
        _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo,
        Options.Create(new LoremasterOptions { MaxRetrievalCount = 30, MaxFactsPerArtifact = 15 }));

    // ------------------------------------------------------------------ Ask/retrieval --

    [Test]
    public async Task Retrieval_PlayerB_DoesNotReceiveAsPrivateKnowledge()
    {
        var context = await CreateRetriever().RetrieveAsync(
            "Tell me about Secret Contact and Black Harbor",
            WorldId, PlayerB, WorldRole.Player, CancellationToken.None);

        Assert.That(context.Artifacts.Select(a => a.Id), Does.Not.Contain(_privateArtifact.Id));
        Assert.That(context.Facts.Select(f => f.Id), Does.Not.Contain(_privateFact.Id));
        Assert.That(context.Relationships.Select(r => r.Id), Does.Not.Contain(_privateRelationship.Id));
        Assert.That(context.Artifacts.Select(a => a.Id), Does.Contain(_partyArtifact.Id),
            "the shared artifact itself is still visible");
    }

    [Test]
    public async Task Retrieval_OwnerAndGm_ReceiveThePrivateKnowledge()
    {
        foreach (var (userId, role) in new[] { (PlayerA, WorldRole.Player), (GmUser, WorldRole.GM) })
        {
            var context = await CreateRetriever().RetrieveAsync(
                "Tell me about Secret Contact and Black Harbor",
                WorldId, userId, role, CancellationToken.None);

            Assert.That(context.Artifacts.Select(a => a.Id), Does.Contain(_privateArtifact.Id), $"{role} sees the artifact");
            Assert.That(context.Facts.Select(f => f.Id), Does.Contain(_privateFact.Id), $"{role} sees the fact");
            Assert.That(context.Relationships.Select(r => r.Id), Does.Contain(_privateRelationship.Id), $"{role} sees the relationship");
        }
    }

    // ------------------------------------------------------------------------ Browse --

    [Test]
    public async Task List_PlayerB_DoesNotSeeAsPrivateArtifact()
    {
        var result = await CreateArtifactService().ListAsync(
            new ArtifactListQuery(WorldId, PlayerB, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Id), Does.Not.Contain(_privateArtifact.Id));
        Assert.That(result.Value!.Select(a => a.Id), Does.Contain(_partyArtifact.Id));
    }

    // ------------------------------------------------------------------------- Graph --

    [Test]
    public async Task Graph_PlayerB_SeesNeitherPrivateNodeNorPrivateEdge()
    {
        var result = await CreateArtifactService().GetGraphAsync(WorldId, PlayerB, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Value!.Nodes.Select(n => n.Id), Does.Not.Contain(_privateArtifact.Id), "node hidden");
        Assert.That(result.Value!.Edges.Select(e => e.Id), Does.Not.Contain(_privateRelationship.Id), "edge hidden");
    }

    [Test]
    public async Task Graph_Owner_SeesPrivateNodeAndEdge()
    {
        var result = await CreateArtifactService().GetGraphAsync(WorldId, PlayerA, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Value!.Nodes.Select(n => n.Id), Does.Contain(_privateArtifact.Id));
        Assert.That(result.Value!.Edges.Select(e => e.Id), Does.Contain(_privateRelationship.Id));
    }

    // ------------------------------------------------------------------------ Detail --

    [Test]
    public async Task Detail_PlayerB_GetsNotFoundForAsPrivateArtifact()
    {
        var result = await CreateArtifactService().GetDetailAsync(
            _privateArtifact.Id, WorldId, PlayerB, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Detail_SharedArtifact_FiltersAsPrivateFactAndRelationshipForPlayerB()
    {
        var result = await CreateArtifactService().GetDetailAsync(
            _partyArtifact.Id, WorldId, PlayerB, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Facts.Select(f => f.Id), Does.Not.Contain(_privateFact.Id));
        Assert.That(result.Value!.Relationships.Select(r => r.Id), Does.Not.Contain(_privateRelationship.Id));
        Assert.That(result.Value!.ConnectedArtifacts.Select(a => a.Id), Does.Not.Contain(_privateArtifact.Id));
    }

    [Test]
    public async Task Detail_SharedArtifact_ShowsAsPrivateContributionsToOwner()
    {
        var result = await CreateArtifactService().GetDetailAsync(
            _partyArtifact.Id, WorldId, PlayerA, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Facts.Select(f => f.Id), Does.Contain(_privateFact.Id));
        Assert.That(result.Value!.Relationships.Select(r => r.Id), Does.Contain(_privateRelationship.Id));
    }

    // ------------------------------------------------------------------------- Canon --

    [Test]
    public async Task Canon_PlayerB_DoesNotSeeAsPrivateEntries()
    {
        var service = new CanonService(_artifactRepo, _factRepo, _relationshipRepo);

        var result = await service.GetCanonAsync(
            new CanonQuery(WorldId, PlayerB, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Id), Does.Not.Contain(_privateFact.Id));
        Assert.That(result.Value!.Select(e => e.Id), Does.Not.Contain(_privateRelationship.Id));
    }

    [Test]
    public async Task Canon_Owner_SeesOwnPrivateEntries()
    {
        var service = new CanonService(_artifactRepo, _factRepo, _relationshipRepo);

        var result = await service.GetCanonAsync(
            new CanonQuery(WorldId, PlayerA, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!.Select(e => e.Id), Does.Contain(_privateFact.Id));
    }

    // ---------------------------------------------------------------------- Timeline --

    [Test]
    public async Task Timeline_PlayerB_DoesNotSeeAsPrivateStorylineLane()
    {
        var privateStoryline = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Storyline,
            Name = "A's Secret Quest", Visibility = VisibilityScope.Private,
            CreatedByUserId = PlayerA, Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(privateStoryline);

        var service = CreateArtifactService();

        var forB = await service.GetStorylineTimelineAsync(WorldId, PlayerB, WorldRole.Player, CancellationToken.None);
        Assert.That(forB.Value!.Lanes.Select(l => l.StorylineId), Does.Not.Contain(privateStoryline.Id));

        var forA = await service.GetStorylineTimelineAsync(WorldId, PlayerA, WorldRole.Player, CancellationToken.None);
        Assert.That(forA.Value!.Lanes.Select(l => l.StorylineId), Does.Contain(privateStoryline.Id));
    }

    // --------------------------------------------------------------------- Stamping --

    [Test]
    public async Task ProposalApplicator_StampsSourceCreatorOnAllThreeEntityTypes()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.SessionNote,
            Title = "A's journal", Visibility = VisibilityScope.Private,
            CreatedByUserId = PlayerA, CreatedAt = DateTimeOffset.UtcNow,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        _sourceRepo.Seed(source);
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceId = source.Id,
            Status = ReviewBatchStatus.Pending, CreatedAt = DateTimeOffset.UtcNow
        };

        var applicator = new ProposalApplicator(
            _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo, new InMemorySourceAttachmentRepository(), new InMemoryMapPlacemarkRepository());

        // CreateArtifact
        var createProposal = MakeProposal(batch.Id, ReviewChangeType.CreateArtifact, null,
            """{"name":"Voss Informant","type":"Character"}""");
        var created = await applicator.ApplyAsync(createProposal, batch, VisibilityFilter.All, CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True);
        var newArtifact = _artifactRepo.Artifacts.Single(a => a.Name == "Voss Informant");
        Assert.That(newArtifact.CreatedByUserId, Is.EqualTo(PlayerA), "artifact carries the source author");

        // AddFact (targeting the new artifact)
        var factProposal = MakeProposal(batch.Id, ReviewChangeType.AddFact, newArtifact.Id,
            """{"predicate":"works at","value":"the docks"}""");
        var addedFact = await applicator.ApplyAsync(factProposal, batch, VisibilityFilter.All, CancellationToken.None);
        Assert.That(addedFact.IsSuccess, Is.True);
        var newFact = _factRepo.Facts.Single(f => f.Predicate == "works at");
        Assert.That(newFact.CreatedByUserId, Is.EqualTo(PlayerA), "fact carries the source author");

        // AddRelationship
        var relProposal = MakeProposal(batch.Id, ReviewChangeType.AddRelationship, null,
            $$"""{"artifactAId":"{{newArtifact.Id}}","artifactBId":"{{_partyArtifact.Id}}","type":"WorksIn"}""");
        var addedRel = await applicator.ApplyAsync(relProposal, batch, VisibilityFilter.All, CancellationToken.None);
        Assert.That(addedRel.IsSuccess, Is.True);
        var newRel = _relationshipRepo.Relationships.Single(r => r.Type == "WorksIn");
        Assert.That(newRel.CreatedByUserId, Is.EqualTo(PlayerA), "relationship carries the source author");
    }

    [Test]
    public async Task SetStorylineParent_PrivateLink_InheritsPrivateEndpointOwner()
    {
        var now = DateTimeOffset.UtcNow;
        var privateChild = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Storyline,
            Name = "A's Secret Quest", Visibility = VisibilityScope.Private,
            CreatedByUserId = PlayerA, Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        var partyParent = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Storyline,
            Name = "The Main Arc", Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        _artifactRepo.Seed(privateChild, partyParent);

        var result = await CreateArtifactService().SetStorylineParentAsync(
            new SetStorylineParentCommand(privateChild.Id, WorldId, GmUser, WorldRole.GM, partyParent.Id),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var link = _relationshipRepo.Relationships.Single(r =>
            r.Type == ArtifactService.PartOfRelationshipType && r.ArtifactAId == privateChild.Id);
        Assert.That(link.Visibility, Is.EqualTo(VisibilityScope.Private), "link is as visible as its least visible endpoint");
        Assert.That(link.CreatedByUserId, Is.EqualTo(PlayerA),
            "the private endpoint's owner keeps their own timeline nesting visible");
    }

    private static ReviewProposal MakeProposal(
        Guid batchId, ReviewChangeType changeType, Guid? targetId, string payloadJson) => new()
    {
        Id = Guid.NewGuid(),
        ReviewBatchId = batchId,
        ChangeType = changeType,
        TargetType = ReviewTargetType.Artifact,
        TargetId = targetId,
        ProposedValueJson = payloadJson,
        Rationale = "test",
        Status = ReviewProposalStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
