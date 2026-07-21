using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// User-authored session→location links. Each is an ordinary <see cref="SourceReference"/>
/// (Artifact target), so it feeds the Journey and Locations maps through the same signal. Only the
/// source's creator or a GM may edit; linking is idempotent and visibility-honest; removal is
/// "remove-any" — an editor may drop even an extractor-authored link.
/// </summary>
[TestFixture]
[Category("Feature: locations")]
public class SourceLocationServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherPlayerId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemorySourceReferenceRepository _refRepo = null!;
    private SourceLocationService _sut = null!;

    private Source _session = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _refRepo = new InMemorySourceReferenceRepository();
        _sut = new SourceLocationService(_sourceRepo, _artifactRepo, _refRepo);

        _session = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.SessionNote, Title = "Session 5",
            Visibility = VisibilityScope.PartyVisible, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = OwnerId, CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(_session);
    }

    private Artifact SeedArtifact(
        ArtifactType type, VisibilityScope visibility, string name = "Saltmere",
        ArtifactStatus status = ArtifactStatus.Active, Guid? worldId = null)
    {
        var a = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = worldId ?? WorldId, Type = type, Name = name,
            Summary = "A harbor town.", Visibility = visibility, Status = status,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(a);
        return a;
    }

    private void SeedRef(Guid artifactId, string? quote = null) => _refRepo.Seed(new SourceReference
    {
        Id = Guid.NewGuid(), SourceId = _session.Id, TargetType = SourceReferenceTargetType.Artifact,
        TargetId = artifactId, Quote = quote, CreatedAt = DateTimeOffset.UtcNow
    });

    [Test]
    public async Task Link_WritesReference_AndReturnsLocation()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);

        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(l => l.ArtifactId), Does.Contain(place.Id));
        // The reference is the signal the Journey trail and Locations tree both read as "visited".
        Assert.That(_refRepo.References.Count(r =>
            r.SourceId == _session.Id && r.TargetType == SourceReferenceTargetType.Artifact && r.TargetId == place.Id),
            Is.EqualTo(1));
    }

    [Test]
    public async Task Link_IsIdempotent()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);

        await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.GM, CancellationToken.None);
        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(_refRepo.References.Count(r => r.SourceId == _session.Id && r.TargetId == place.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task Link_IsIdempotent_OverAnExtractorAuthoredReference()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);
        SeedRef(place.Id, quote: "we made port at Saltmere"); // already found by extraction

        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_refRepo.References.Count(r => r.SourceId == _session.Id && r.TargetId == place.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task Link_NonLocationArtifact_Returns400()
    {
        var character = SeedArtifact(ArtifactType.Character, VisibilityScope.PartyVisible, name: "Harbourmaster Vane");

        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, character.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_a_location"));
        Assert.That(_refRepo.References, Is.Empty);
    }

    [Test]
    public async Task Link_ArtifactInAnotherWorld_Returns400()
    {
        var foreign = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible, worldId: Guid.NewGuid());

        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, foreign.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_a_location"));
    }

    [Test]
    public async Task Link_GmOnlyLocation_HiddenFromPlayer_Returns400()
    {
        var secret = SeedArtifact(ArtifactType.Location, VisibilityScope.GMOnly, name: "Smuggler's Cove");

        // The creating player cannot see the GM-only place, so cannot link it (and it does not leak).
        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, secret.Id, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_a_location"));
    }

    [Test]
    public async Task Link_ByNonCreatorPlayer_Returns403()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);

        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"));
        Assert.That(_refRepo.References, Is.Empty);
    }

    [Test]
    public async Task Link_ByObserver_Returns403()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);

        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
    }

    [Test]
    public async Task Link_ByCreatorPlayer_Succeeds()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);

        // The creator, though only a Player, may edit their own source's locations.
        var result = await _sut.LinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task Unlink_RemovesAnyLink_IncludingExtractorAuthored()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);
        SeedRef(place.Id, quote: "we made port at Saltmere"); // extractor-authored, indistinguishable

        var result = await _sut.UnlinkLocationAsync(_session.Id, WorldId, place.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Is.Empty);
        Assert.That(_refRepo.References, Is.Empty);
    }

    [Test]
    public async Task Unlink_ByNonCreatorPlayer_Returns403()
    {
        var place = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible);
        SeedRef(place.Id);

        var result = await _sut.UnlinkLocationAsync(_session.Id, WorldId, place.Id, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"));
        Assert.That(_refRepo.References, Has.Count.EqualTo(1)); // untouched
    }

    [Test]
    public async Task List_ReturnsOnlyCallerVisibleLocations_AndIgnoresNonLocations()
    {
        var party = SeedArtifact(ArtifactType.Location, VisibilityScope.PartyVisible, name: "Black Harbor");
        var secret = SeedArtifact(ArtifactType.Location, VisibilityScope.GMOnly, name: "Smuggler's Cove");
        var character = SeedArtifact(ArtifactType.Character, VisibilityScope.PartyVisible, name: "Vane");
        SeedRef(party.Id);
        SeedRef(secret.Id);
        SeedRef(character.Id); // a non-location reference must never surface as a linked location

        var asPlayer = await _sut.ListLocationsAsync(_session.Id, WorldId, OwnerId, WorldRole.Player, CancellationToken.None);
        var asGm = await _sut.ListLocationsAsync(_session.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(asPlayer.Value!.Select(l => l.ArtifactId), Is.EquivalentTo(new[] { party.Id }));
        Assert.That(asGm.Value!.Select(l => l.ArtifactId), Is.EquivalentTo(new[] { party.Id, secret.Id }));
    }

    [Test]
    public async Task List_SourceNotVisibleToCaller_Returns404()
    {
        var gmOnly = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.SessionNote, Title = "GM prep",
            Visibility = VisibilityScope.GMOnly, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = OwnerId, CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(gmOnly);

        var result = await _sut.ListLocationsAsync(gmOnly.Id, WorldId, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_found"));
    }
}
