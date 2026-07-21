using Nornis.Application.Errors;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The journey read model: pins come from the map viewer (visibility-honest), and stops are the
/// visible dated sessions that referenced those pins, in order. A player and a GM get different
/// journeys from the same data.
/// </summary>
[TestFixture]
[Category("Feature: journey-map")]
public class JourneyMapServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();

    private static readonly DateTimeOffset Jan = new(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);

    private InMemorySourceRepository _sources = null!;
    private InMemorySourceAttachmentRepository _attachments = null!;
    private InMemoryMapPlacemarkRepository _placemarks = null!;
    private InMemoryArtifactRepository _artifacts = null!;
    private InMemorySourceReferenceRepository _references = null!;
    private FakeBlobStorageService _blob = null!;
    private JourneyMapService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sources = new InMemorySourceRepository();
        _attachments = new InMemorySourceAttachmentRepository();
        _placemarks = new InMemoryMapPlacemarkRepository();
        _artifacts = new InMemoryArtifactRepository();
        _references = new InMemorySourceReferenceRepository();
        _blob = new FakeBlobStorageService();

        var mapView = new MapViewService(_sources, _attachments, _placemarks, _artifacts, _blob);
        _sut = new JourneyMapService(mapView, _sources, _references, _artifacts);
    }

    // ------------------------------------------------------------------ seeds --

    private (Source Source, SourceAttachment Map) SeedMap(
        VisibilityScope visibility = VisibilityScope.PartyVisible, DateTimeOffset? occurredAt = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.Map, Title = "Map",
            Visibility = visibility, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = GmId, CreatedAt = DateTimeOffset.UtcNow, OccurredAt = occurredAt
        };
        _sources.Seed(source);
        var map = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = source.Id, WorldId = WorldId,
            Kind = SourceAttachmentKind.MapImage, FileName = "map.png", ContentType = "image/png",
            SizeBytes = 3, BlobPath = $"b/{source.Id}", Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachments.Seed(map);
        return (source, map);
    }

    private Artifact SeedArtifact(string name, ArtifactType type = ArtifactType.Location,
        VisibilityScope visibility = VisibilityScope.PartyVisible, Guid? owner = null,
        ArtifactStatus status = ArtifactStatus.Active, string? summary = null)
    {
        var a = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = type, Name = name,
            Visibility = visibility, CreatedByUserId = owner, Status = status, Summary = summary,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifacts.Seed(a);
        return a;
    }

    private void SeedPin(Guid mapAttachmentId, Guid artifactId) => _placemarks.Seed(new MapPlacemark
    {
        Id = Guid.NewGuid(), WorldId = WorldId, SourceAttachmentId = mapAttachmentId, ArtifactId = artifactId,
        X = 0.5m, Y = 0.5m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    });

    private Source SeedSession(DateTimeOffset? occurredAt,
        VisibilityScope visibility = VisibilityScope.PartyVisible, Guid? owner = null,
        string title = "Session", SourceType type = SourceType.SessionNote)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = type, Title = title,
            Visibility = visibility, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = owner ?? GmId, CreatedAt = DateTimeOffset.UtcNow, OccurredAt = occurredAt
        };
        _sources.Seed(source);
        return source;
    }

    // A session's SourceReference to an artifact — the visit signal for a Location, and the
    // provenance for any highlight.
    private void SeedTouch(Guid sessionId, Guid artifactId) => _references.Seed(new SourceReference
    {
        Id = Guid.NewGuid(), SourceId = sessionId, TargetType = SourceReferenceTargetType.Artifact,
        TargetId = artifactId, CreatedAt = DateTimeOffset.UtcNow
    });

    private Task<AppResult<JourneyMap>> Run(Guid? mapSourceId, Guid userId, WorldRole role) =>
        _sut.GetJourneyAsync(WorldId, mapSourceId, userId, role, CancellationToken.None);

    // ------------------------------------------------------------------ tests --

    [Test]
    public async Task AutoPick_ChoosesMapWithMostVisiblePins()
    {
        var (_, thin) = SeedMap();
        var (_, rich) = SeedMap();
        SeedPin(thin.Id, SeedArtifact("Lone Isle").Id);
        SeedPin(rich.Id, SeedArtifact("Black Harbor").Id);
        SeedPin(rich.Id, SeedArtifact("Saltmere").Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.MapAttachmentId, Is.EqualTo(rich.Id));
        Assert.That(result.Value.Locations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ExplicitMapSourceId_UsesThatMap()
    {
        var (thinSource, thin) = SeedMap();
        var (_, rich) = SeedMap();
        SeedPin(thin.Id, SeedArtifact("Lone Isle").Id);
        SeedPin(rich.Id, SeedArtifact("Black Harbor").Id);
        SeedPin(rich.Id, SeedArtifact("Saltmere").Id);

        var result = await Run(mapSourceId: thinSource.Id, GmId, WorldRole.GM);

        Assert.That(result.Value!.MapAttachmentId, Is.EqualTo(thin.Id));
        Assert.That(result.Value.Locations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ExplicitMapSourceId_InvisibleToPlayer_Returns404()
    {
        var (gmMap, map) = SeedMap(visibility: VisibilityScope.GMOnly);
        SeedPin(map.Id, SeedArtifact("Secret Vault").Id);

        var result = await Run(mapSourceId: gmMap.Id, PlayerId, WorldRole.Player);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task NoVisibleMapWithPins_Returns_no_map()
    {
        SeedMap(); // a map, but nothing pinned on it

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("no_map"));
    }

    [Test]
    public async Task Stops_AreOrderedByOccurredAt()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        // Seed out of chronological order.
        foreach (var d in new[] { Mar, Jan, Feb })
        {
            SeedTouch(SeedSession(d).Id, loc.Id);
        }

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        var dates = result.Value!.Stops.Select(s => s.OccurredAt).ToList();
        Assert.That(dates, Is.EqualTo(new[] { Jan, Feb, Mar }));
    }

    [Test]
    public async Task UndatedVisitingSession_IsExcludedAndCounted()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        SeedTouch(SeedSession(Jan).Id, loc.Id);
        SeedTouch(SeedSession(occurredAt: null).Id, loc.Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.Value!.Stops, Has.Count.EqualTo(1));
        Assert.That(result.Value.UndatedSessionCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LocationReferencedTwiceInOneSession_IsCountedOnce()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        var session = SeedSession(Jan);
        SeedTouch(session.Id, loc.Id);
        SeedTouch(session.Id, loc.Id); // duplicate reference

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.Value!.Stops, Has.Count.EqualTo(1));
        Assert.That(result.Value.Stops[0].VisitedLocationIds, Is.EqualTo(new[] { loc.Id }));
    }

    [Test]
    public async Task PlayerAndGm_GetDifferentJourneysFromSameData()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        SeedTouch(SeedSession(Jan, visibility: VisibilityScope.PartyVisible).Id, loc.Id);
        SeedTouch(SeedSession(Feb, visibility: VisibilityScope.GMOnly).Id, loc.Id);

        var gm = await Run(mapSourceId: null, GmId, WorldRole.GM);
        var player = await Run(mapSourceId: null, PlayerId, WorldRole.Player);

        Assert.That(gm.Value!.Stops, Has.Count.EqualTo(2));
        Assert.That(player.Value!.Stops, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GmOnlyPin_IsHiddenFromPlayer()
    {
        var (_, map) = SeedMap();
        var secret = SeedArtifact("Voss's Safehouse", visibility: VisibilityScope.GMOnly);
        SeedPin(map.Id, secret.Id);
        var known = SeedArtifact("Black Harbor");
        SeedPin(map.Id, known.Id);

        var gm = await Run(mapSourceId: null, GmId, WorldRole.GM);
        var player = await Run(mapSourceId: null, PlayerId, WorldRole.Player);

        Assert.That(gm.Value!.Locations.Select(l => l.Name), Does.Contain("Voss's Safehouse"));
        Assert.That(player.Value!.Locations.Select(l => l.Name), Does.Not.Contain("Voss's Safehouse"));
        Assert.That(player.Value.Locations.Select(l => l.Name), Does.Contain("Black Harbor"));
    }

    [Test]
    public async Task FirstSeen_MarksTheEarliestVisibleStopForAnArtifact()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        SeedTouch(SeedSession(Jan).Id, loc.Id);
        SeedTouch(SeedSession(Feb).Id, loc.Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        var first = result.Value!.Stops[0].Highlights.Single(h => h.ArtifactId == loc.Id);
        var later = result.Value.Stops[1].Highlights.Single(h => h.ArtifactId == loc.Id);
        Assert.That(first.FirstSeen, Is.True);
        Assert.That(later.FirstSeen, Is.False);
    }

    [Test]
    public async Task Highlights_IncludeNonLocationArtifactsTheSessionTouched()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        var evt = SeedArtifact("Hired to find the caravan", ArtifactType.Event);
        var session = SeedSession(Jan);
        SeedTouch(session.Id, loc.Id);
        SeedTouch(session.Id, evt.Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        var types = result.Value!.Stops[0].Highlights.Select(h => h.Type).ToList();
        Assert.That(types, Does.Contain("Location"));
        Assert.That(types, Does.Contain("Event"));
    }

    [Test]
    public async Task ImportedNote_IsIncludedOnTheTimeline()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        var note = SeedSession(Feb, type: SourceType.ImportedNote, title: "Imported lore");
        SeedTouch(note.Id, loc.Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.Value!.Stops, Has.Count.EqualTo(1));
        Assert.That(result.Value.Stops[0].Title, Is.EqualTo("Imported lore"));
        Assert.That(result.Value.Stops[0].VisitedLocationIds, Is.EqualTo(new[] { loc.Id }));
    }

    [Test]
    public async Task NonSessionSource_ThatVisitedAPin_IsNotAStop()
    {
        // A GM note can reference a pinned place, but the timeline is sessions and imported notes
        // only — it must not surface a GM aside (or an upload, web link, reveal, …) as a stop.
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor");
        SeedPin(map.Id, loc.Id);
        var gmNote = SeedSession(Jan, type: SourceType.GMNote);
        SeedTouch(gmNote.Id, loc.Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.Value!.Stops, Is.Empty);
    }

    [Test]
    public async Task DatedSession_ThatVisitedNoPinnedPlace_IsStillAStopWithNoVisits()
    {
        var (_, map) = SeedMap();
        SeedPin(map.Id, SeedArtifact("Black Harbor").Id); // a pin so the map can anchor a journey
        var stayIn = SeedSession(Jan); // references nothing on the map

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        Assert.That(result.Value!.Stops, Has.Count.EqualTo(1));
        Assert.That(result.Value.Stops[0].SourceId, Is.EqualTo(stayIn.Id));
        Assert.That(result.Value.Stops[0].VisitedLocationIds, Is.Empty);
    }

    [Test]
    public async Task Highlight_CarriesTheArtifactSummary()
    {
        var (_, map) = SeedMap();
        var loc = SeedArtifact("Black Harbor", summary: "A fog-wrapped port on the northern reach.");
        SeedPin(map.Id, loc.Id);
        SeedTouch(SeedSession(Jan).Id, loc.Id);

        var result = await Run(mapSourceId: null, GmId, WorldRole.GM);

        var highlight = result.Value!.Stops[0].Highlights.Single(h => h.ArtifactId == loc.Id);
        Assert.That(highlight.Summary, Is.EqualTo("A fog-wrapped port on the northern reach."));
    }
}
