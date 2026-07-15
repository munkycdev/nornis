using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactServiceRenameTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private ArtifactService _service = null!;

    private Guid _worldId;
    private Guid _otherWorldId;
    private Guid _gmUserId;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _service = new ArtifactService(
            _artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemorySourceRepository());

        _worldId = Guid.NewGuid();
        _otherWorldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private Artifact SeedArtifact(string name = "Captain Voss", Guid? worldId = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _worldId,
            Type = ArtifactType.Character,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private RenameArtifactCommand Command(Guid artifactId, string name, WorldRole role = WorldRole.GM) =>
        new(artifactId, _worldId, _gmUserId, role, name);

    [Test]
    public async Task RenameAsync_Gm_RenamesTrimsAndBumpsUpdatedAt()
    {
        var artifact = SeedArtifact();
        var before = artifact.UpdatedAt;

        var result = await _service.RenameAsync(Command(artifact.Id, "  Captain Ilsa Voss  "), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Name, Is.EqualTo("Captain Ilsa Voss"));
        Assert.That(result.Value.UpdatedAt, Is.GreaterThan(before));
    }

    [Test]
    public async Task RenameAsync_Player_IsRejected()
    {
        var artifact = SeedArtifact();

        var result = await _service.RenameAsync(Command(artifact.Id, "New Name", WorldRole.Player), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
        Assert.That(result.Error.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task RenameAsync_Observer_IsRejected()
    {
        var artifact = SeedArtifact();

        var result = await _service.RenameAsync(Command(artifact.Id, "New Name", WorldRole.Observer), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task RenameAsync_EmptyOrWhitespaceName_IsRejected(string name)
    {
        var artifact = SeedArtifact();

        var result = await _service.RenameAsync(Command(artifact.Id, name), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
        Assert.That(result.Error.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task RenameAsync_NameOver200Chars_IsRejected()
    {
        var artifact = SeedArtifact();

        var result = await _service.RenameAsync(Command(artifact.Id, new string('x', 201)), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task RenameAsync_MissingArtifact_IsNotFound()
    {
        var result = await _service.RenameAsync(Command(Guid.NewGuid(), "New Name"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_found"));
        Assert.That(result.Error.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RenameAsync_ArtifactInAnotherWorld_IsNotFound()
    {
        var artifact = SeedArtifact(worldId: _otherWorldId);

        var result = await _service.RenameAsync(Command(artifact.Id, "New Name"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_found"));
    }
}
