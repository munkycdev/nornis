using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Public;

/// <summary>
/// A reveal record is party-visible and not public. Every public read that can reach a source
/// — the list, the source by id, its knowledge, its locations, an entry's citations — has to
/// refuse it, and refuse it the way it refuses anything unavailable. Sabotage: make
/// <c>PublicSurface.ShowsSource</c> return true; every assertion here but the control goes red.
/// </summary>
[TestFixture]
public class PublicRevealExclusionTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private async Task<SourceTestScenario> SetupPublicWorldAsync()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var update = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}",
            new UpdateWorldRequest(PublicSlug: "black-harbor", PublicAccessEnabled: true));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
        return scenario;
    }

    private async Task<Guid> SeedSourceAsync(Guid worldId, Guid gmUserId, SourceType type, string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = type,
            Title = title,
            Body = "The harbor burned.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = gmUserId,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    [Test]
    [Category("Authorization")]
    public async Task Reveal_IsAbsentFromTheList_And404ByIdEverywhere()
    {
        var scenario = await SetupPublicWorldAsync();
        var session = await SeedSourceAsync(scenario.World.Id, scenario.GmUserId, SourceType.SessionNote, "Session 1");
        var reveal = await SeedSourceAsync(scenario.World.Id, scenario.GmUserId, SourceType.Reveal, "Revealed: the mayor");

        var list = await _anonymous.GetFromJsonAsync<List<SourceListItemResponse>>("/api/public/worlds/black-harbor/sources");
        var sessionDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{session}");
        var revealDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{reveal}");
        var revealKnowledge = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{reveal}/knowledge");
        var revealLocations = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{reveal}/locations");
        var unknownDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{Guid.NewGuid()}");

        var revealBody = await revealDetail.Content.ReadAsStringAsync();
        var unknownBody = await unknownDetail.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(list!.Select(s => s.Id), Does.Contain(session), "the control: an ordinary session is listed");
            Assert.That(list!.Select(s => s.Id), Does.Not.Contain(reveal));
            Assert.That(sessionDetail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(revealDetail.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(revealKnowledge.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(revealLocations.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(revealBody, Is.EqualTo(unknownBody), "a reveal and a nonexistent source answer identically");
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task Reveal_IsAbsentFromAnEntrysCitations_OrdinaryCitationsStay()
    {
        var scenario = await SetupPublicWorldAsync();
        var session = await SeedSourceAsync(scenario.World.Id, scenario.GmUserId, SourceType.SessionNote, "Session 1");
        var reveal = await SeedSourceAsync(scenario.World.Id, scenario.GmUserId, SourceType.Reveal, "Revealed: the mayor");
        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, scenario.World.Id, "Captain Voss");
        await KnowledgeTestHelpers.CreateTestSourceReferenceAsync(_factory, session, SourceReferenceTargetType.Artifact, voss.Id, quote: "sailed at dawn");
        await KnowledgeTestHelpers.CreateTestSourceReferenceAsync(_factory, reveal, SourceReferenceTargetType.Artifact, voss.Id, quote: "is the mayor's man");

        var detail = await _anonymous.GetFromJsonAsync<ArtifactDetailResponse>(
            $"/api/public/worlds/black-harbor/artifacts/{voss.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(detail!.SourceReferences.Select(r => r.SourceId), Does.Contain(session),
                "the control — if the source is not loaded on the reference, this fails closed and this line says so");
            Assert.That(detail.SourceReferences.Select(r => r.SourceId), Does.Not.Contain(reveal));
        });
    }
}
