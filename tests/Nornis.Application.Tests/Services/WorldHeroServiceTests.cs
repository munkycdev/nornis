using Nornis.Application.Errors;
using Nornis.Application.Hero;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The hero (feature 42) decides its picture once, on the server: key art, else the journey map
/// as the caller's own role sees it, else none. Its counts are the caller's.
/// </summary>
[TestFixture]
public class WorldHeroServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Stamp = DateTimeOffset.Parse("2026-11-07T20:00:00Z");

    private InMemoryWorldRepository _worlds = null!;
    private ISourceRepository _sources = null!;
    private IArtifactService _artifacts = null!;
    private IJourneyMapService _journey = null!;
    private WorldHeroService _sut = null!;
    private World _world = null!;

    [SetUp]
    public async Task SetUp()
    {
        _worlds = new InMemoryWorldRepository();
        _sources = Substitute.For<ISourceRepository>();
        _artifacts = Substitute.For<IArtifactService>();
        _journey = Substitute.For<IJourneyMapService>();
        _sut = new WorldHeroService(_worlds, _sources, _artifacts, _journey);
        _world = await _worlds.CreateAsync(new World { Id = Guid.NewGuid(), Name = "Black Harbor", GameSystem = "Shadowdark", CreatedByUserId = UserId });

        _sources.ListSummariesByWorldAsync(_world.Id, Arg.Any<Guid>(), Arg.Any<WorldRole>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns([]);
        _artifacts.ListAsync(Arg.Any<ArtifactListQuery>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Artifact>>.Success([]));
        NoMap();
    }

    private void NoMap() =>
        _journey.GetJourneyAsync(_world.Id, null, Arg.Any<Guid>(), Arg.Any<WorldRole>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<JourneyMap>.Fail(new AppError(404, "no_map", "No map.")));

    private void MapFor(WorldRole role, string url) =>
        _journey.GetJourneyAsync(_world.Id, null, Arg.Any<Guid>(), role, Arg.Any<CancellationToken>())
            .Returns(AppResult<JourneyMap>.Success(new JourneyMap(Guid.NewGuid(), Guid.NewGuid(), url, [], [], 0)));

    private async Task<WorldHero> HeroAsync(WorldRole role = WorldRole.GM) =>
        (await _sut.GetAsync(_world.Id, UserId, role, default)).Value!;

    [Test]
    public async Task KeyArt_BeatsTheMap_AndCarriesItsStampAndFocus()
    {
        MapFor(WorldRole.GM, "https://maps/harbor.png");
        _world.KeyArt = new WorldKeyArt
        {
            BlobPath = "worlds/x/key-art/uploads/y.png",
            ContentType = "image/png",
            Source = CoverSource.Uploaded,
            Anchor = KeyArtAnchor.Bottom,
            UpdatedAt = Stamp,
        };

        var hero = await HeroAsync();

        Assert.That(hero.Picture, Is.EqualTo(new HeroPicture(HeroPictureKind.KeyArt, Stamp, null, 100)));
    }

    [Test]
    public async Task WithNoKeyArt_TheMapIsTheOneTheCallersRoleSees()
    {
        MapFor(WorldRole.GM, "https://maps/gm-only.png");

        var gm = await HeroAsync(WorldRole.GM);
        var player = await HeroAsync(WorldRole.Player);

        Assert.Multiple(() =>
        {
            Assert.That(gm.Picture.Kind, Is.EqualTo(HeroPictureKind.Map));
            Assert.That(gm.Picture.MapUrl, Is.EqualTo("https://maps/gm-only.png"));
            Assert.That(player.Picture, Is.EqualTo(HeroPicture.None), "the player's Map page shows no map, so neither does the hero");
        });
        await _journey.Received().GetJourneyAsync(_world.Id, null, UserId, WorldRole.Player, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheFacts_CountDatedSessions_AndTheCallersEntries()
    {
        var early = DateTimeOffset.Parse("2026-10-01T19:00:00Z");
        var late = DateTimeOffset.Parse("2026-11-01T19:00:00Z");
        _sources.ListSummariesByWorldAsync(_world.Id, UserId, WorldRole.Player, cancellationToken: Arg.Any<CancellationToken>())
            .Returns([
                Source(SourceType.SessionNote, early),
                Source(SourceType.SessionNote, late),
                Source(SourceType.SessionNote, null),
                Source(SourceType.JournalEntry, late.AddDays(3)),
            ]);
        _artifacts.ListAsync(Arg.Is<ArtifactListQuery>(q => q.ActingUserRole == WorldRole.Player), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Artifact>>.Success([new Artifact(), new Artifact(), new Artifact()]));

        var hero = await HeroAsync(WorldRole.Player);

        Assert.Multiple(() =>
        {
            Assert.That(hero.Name, Is.EqualTo("Black Harbor"));
            Assert.That(hero.GameSystem, Is.EqualTo("Shadowdark"));
            Assert.That(hero.SessionCount, Is.EqualTo(2), "an undated note and a journal aren't sessions played");
            Assert.That(hero.LastSessionAt, Is.EqualTo(late));
            Assert.That(hero.EntryCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ThePublicHero_ReadsAsAnObserver_AndOnlyForAPublicWorld()
    {
        var hidden = await _sut.GetForPublicAsync(_world, default);
        _world.PublicAccessEnabled = true;
        var shown = await _sut.GetForPublicAsync(_world, default);

        Assert.Multiple(() =>
        {
            Assert.That(hidden.Error!.StatusCode, Is.EqualTo(404));
            Assert.That(shown.IsSuccess, Is.True);
        });
        await _journey.Received().GetJourneyAsync(_world.Id, null, Guid.Empty, WorldRole.Observer, Arg.Any<CancellationToken>());
    }

    [TestCase(KeyArtAnchor.Top, 0)]
    [TestCase(KeyArtAnchor.Centre, 50)]
    [TestCase(KeyArtAnchor.Bottom, 100)]
    public void TheAnchor_IsAVerticalFocus(KeyArtAnchor anchor, int focus) =>
        Assert.That(WorldHeroService.FocusY(anchor), Is.EqualTo(focus));

    private SourceListItem Source(SourceType type, DateTimeOffset? occurredAt) =>
        new(Guid.NewGuid(), _world.Id, type, "A source", occurredAt, Stamp, UserId, VisibilityScope.PartyVisible,
            SourceProcessingStatus.Processed, null, null);
}
