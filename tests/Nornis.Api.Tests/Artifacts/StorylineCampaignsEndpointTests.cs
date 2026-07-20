using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Artifacts;

[TestFixture]
public class StorylineCampaignsEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Campaign> SeedCampaignAsync(Guid worldId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = name,
            Status = CampaignStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = Guid.NewGuid()
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    private static string Url(Guid worldId, Guid artifactId) =>
        $"/api/worlds/{worldId}/artifacts/{artifactId}/campaigns";

    [Test]
    public async Task SetCampaigns_AsGm_DeclaresThem_AndDetailReflectsIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var storyline = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "The Long Road", type: ArtifactType.Storyline);
        var alpha = await SeedCampaignAsync(scenario.World.Id, "Alpha");
        var beta = await SeedCampaignAsync(scenario.World.Id, "Beta");

        var response = await scenario.GmClient.PutAsJsonAsync(
            Url(scenario.World.Id, storyline.Id),
            new { campaignIds = new[] { alpha.Id, beta.Id } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await scenario.GmClient.GetFromJsonAsync<ArtifactDetailResponse>(
            $"/api/worlds/{scenario.World.Id}/artifacts/{storyline.Id}");
        Assert.That(detail!.DeclaredCampaigns.Select(c => c.Name), Is.EquivalentTo(new[] { "Alpha", "Beta" }));
    }

    [Test]
    public async Task SetCampaigns_EmptyList_ClearsDeclarations()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var storyline = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "The Long Road", type: ArtifactType.Storyline);
        var alpha = await SeedCampaignAsync(scenario.World.Id, "Alpha");

        await scenario.GmClient.PutAsJsonAsync(
            Url(scenario.World.Id, storyline.Id), new { campaignIds = new[] { alpha.Id } });

        var clear = await scenario.GmClient.PutAsJsonAsync(
            Url(scenario.World.Id, storyline.Id), new { campaignIds = Array.Empty<Guid>() });

        Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var detail = await scenario.GmClient.GetFromJsonAsync<ArtifactDetailResponse>(
            $"/api/worlds/{scenario.World.Id}/artifacts/{storyline.Id}");
        Assert.That(detail!.DeclaredCampaigns, Is.Empty);
    }

    [Test]
    public async Task SetCampaigns_AsPlayer_IsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var storyline = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "The Long Road", type: ArtifactType.Storyline);
        var alpha = await SeedCampaignAsync(scenario.World.Id, "Alpha");

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            Url(scenario.World.Id, storyline.Id),
            new { campaignIds = new[] { alpha.Id } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task SetCampaigns_NonStoryline_ReturnsNotFound()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var location = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Black Harbor", type: ArtifactType.Location);
        var alpha = await SeedCampaignAsync(scenario.World.Id, "Alpha");

        var response = await scenario.GmClient.PutAsJsonAsync(
            Url(scenario.World.Id, location.Id),
            new { campaignIds = new[] { alpha.Id } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SetCampaigns_CampaignFromAnotherWorld_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var storyline = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "The Long Road", type: ArtifactType.Storyline);
        var foreign = await SeedCampaignAsync(Guid.NewGuid(), "Elsewhere");

        var response = await scenario.GmClient.PutAsJsonAsync(
            Url(scenario.World.Id, storyline.Id),
            new { campaignIds = new[] { foreign.Id } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
