using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactServiceHierarchyTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private ArtifactService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _service = new ArtifactService(_artifactRepo, new InMemoryArtifactFactRepository(), _relationshipRepo,
            new InMemorySourceReferenceRepository(), new InMemorySourceRepository(),
            new InMemoryCharacterRepository(), new InMemoryWorldMemberRepository());

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private Artifact SeedStoryline(string name, VisibilityScope visibility = VisibilityScope.PartyVisible, ArtifactType type = ArtifactType.Storyline)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private SetStorylineParentCommand Command(Guid childId, Guid? parentId, WorldRole role = WorldRole.GM) =>
        new(childId, _worldId, _gmUserId, role, parentId);

    [Test]
    public async Task SetParent_CreatesPartOfRelationship()
    {
        var child = SeedStoryline("Sub-arc");
        var parent = SeedStoryline("Main arc");

        var result = await _service.SetStorylineParentAsync(Command(child.Id, parent.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var relationship = _relationshipRepo.Relationships.Single();
        Assert.That(relationship.Type, Is.EqualTo(ArtifactService.PartOfRelationshipType));
        Assert.That(relationship.ArtifactAId, Is.EqualTo(child.Id));
        Assert.That(relationship.ArtifactBId, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task SetParent_ReplacesExistingParent()
    {
        var child = SeedStoryline("Sub-arc");
        var first = SeedStoryline("First parent");
        var second = SeedStoryline("Second parent");

        await _service.SetStorylineParentAsync(Command(child.Id, first.Id), CancellationToken.None);
        var result = await _service.SetStorylineParentAsync(Command(child.Id, second.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var relationship = _relationshipRepo.Relationships.Single(r => r.Type == ArtifactService.PartOfRelationshipType);
        Assert.That(relationship.ArtifactBId, Is.EqualTo(second.Id));
    }

    [Test]
    public async Task SetParent_NullClearsTheParent()
    {
        var child = SeedStoryline("Sub-arc");
        var parent = SeedStoryline("Main arc");
        await _service.SetStorylineParentAsync(Command(child.Id, parent.Id), CancellationToken.None);

        var result = await _service.SetStorylineParentAsync(Command(child.Id, null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_relationshipRepo.Relationships.Where(r => r.Type == ArtifactService.PartOfRelationshipType), Is.Empty);
    }

    [Test]
    public async Task SetParent_RejectsCycles()
    {
        var grandparent = SeedStoryline("Campaign arc");
        var parent = SeedStoryline("Arc");
        var child = SeedStoryline("Sub-arc");
        await _service.SetStorylineParentAsync(Command(parent.Id, grandparent.Id), CancellationToken.None);
        await _service.SetStorylineParentAsync(Command(child.Id, parent.Id), CancellationToken.None);

        var result = await _service.SetStorylineParentAsync(Command(grandparent.Id, child.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("cycle"));
        Assert.That(result.Error.StatusCode, Is.EqualTo(409));
    }

    // Nothing in the schema stops a storyline from collecting more than one PartOf row —
    // the index on ArtifactAId is non-unique, and the AI backfill/extraction paths can each
    // write one. The GM-facing parent editor has to survive that data and converge it.
    private ArtifactRelationship SeedPartOf(Artifact child, Artifact parent)
    {
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = child.Id,
            ArtifactBId = parent.Id,
            Type = ArtifactService.PartOfRelationshipType,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _relationshipRepo.Seed(relationship);
        return relationship;
    }

    [Test]
    public async Task SetParent_SucceedsWhenAnUnrelatedStorylineHasDuplicateParentRows()
    {
        // The duplicate sits on a storyline nobody is editing — it still poisoned the
        // world-wide cycle guard, so every parent assignment in the world threw.
        var strayChild = SeedStoryline("Stray arc");
        SeedPartOf(strayChild, SeedStoryline("First parent"));
        SeedPartOf(strayChild, SeedStoryline("Second parent"));

        var child = SeedStoryline("Sub-arc");
        var parent = SeedStoryline("Main arc");

        var result = await _service.SetStorylineParentAsync(Command(child.Id, parent.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task SetParent_CollapsesDuplicateParentRowsOnTheEditedChild()
    {
        var child = SeedStoryline("Sub-arc");
        SeedPartOf(child, SeedStoryline("First parent"));
        SeedPartOf(child, SeedStoryline("Second parent"));
        var intended = SeedStoryline("Intended parent");

        var result = await _service.SetStorylineParentAsync(Command(child.Id, intended.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var partOf = _relationshipRepo.Relationships
            .Where(r => r.Type == ArtifactService.PartOfRelationshipType && r.ArtifactAId == child.Id)
            .ToList();
        Assert.That(partOf, Has.Count.EqualTo(1));
        Assert.That(partOf[0].ArtifactBId, Is.EqualTo(intended.Id));
    }

    [Test]
    public async Task SetParent_NullClearsEveryDuplicateParentRow()
    {
        var child = SeedStoryline("Sub-arc");
        SeedPartOf(child, SeedStoryline("First parent"));
        SeedPartOf(child, SeedStoryline("Second parent"));

        var result = await _service.SetStorylineParentAsync(Command(child.Id, null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_relationshipRepo.Relationships.Where(r => r.Type == ArtifactService.PartOfRelationshipType), Is.Empty);
    }

    [Test]
    public async Task SetParent_RejectsSelf()
    {
        var arc = SeedStoryline("Arc");

        var result = await _service.SetStorylineParentAsync(Command(arc.Id, arc.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_parent"));
    }

    [Test]
    public async Task SetParent_RejectsNonStorylineParent()
    {
        var child = SeedStoryline("Sub-arc");
        var location = SeedStoryline("Black Harbor", type: ArtifactType.Location);

        var result = await _service.SetStorylineParentAsync(Command(child.Id, location.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_parent"));
    }

    [Test]
    public async Task SetParent_NonGm_Returns403()
    {
        var child = SeedStoryline("Sub-arc");
        var parent = SeedStoryline("Main arc");

        var result = await _service.SetStorylineParentAsync(Command(child.Id, parent.Id, WorldRole.Player), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task SetParent_GmOnlyEndpointNarrowsLinkVisibility()
    {
        var child = SeedStoryline("Sub-arc", VisibilityScope.GMOnly);
        var parent = SeedStoryline("Main arc");

        await _service.SetStorylineParentAsync(Command(child.Id, parent.Id), CancellationToken.None);

        Assert.That(_relationshipRepo.Relationships.Single().Visibility, Is.EqualTo(VisibilityScope.GMOnly));
    }

    [Test]
    public async Task SetStatus_GmSetsStatus()
    {
        var arc = SeedStoryline("Arc");

        var result = await _service.SetStatusAsync(
            new SetArtifactStatusCommand(arc.Id, _worldId, _gmUserId, WorldRole.GM, ArtifactStatus.Resolved),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ArtifactStatus.Resolved));
    }

    [Test]
    public async Task SetStatus_NonGm_Returns403()
    {
        var arc = SeedStoryline("Arc");

        var result = await _service.SetStatusAsync(
            new SetArtifactStatusCommand(arc.Id, _worldId, _gmUserId, WorldRole.Player, ArtifactStatus.Resolved),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task SetStatus_MissingArtifact_Returns404()
    {
        var result = await _service.SetStatusAsync(
            new SetArtifactStatusCommand(Guid.NewGuid(), _worldId, _gmUserId, WorldRole.GM, ArtifactStatus.Dormant),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }
}
