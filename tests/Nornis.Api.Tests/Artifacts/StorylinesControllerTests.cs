using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Artifacts;

[TestFixture]
public class StorylinesControllerTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task List_ReturnsOnlyStorylineArtifacts()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Captain Voss", type: ArtifactType.Character);
        var caravan = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Missing Caravan", type: ArtifactType.Storyline);
        var prophecy = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "The Drowned Prophecy", type: ArtifactType.Storyline);

        var response = await scenario.GmClient.GetAsync($"/api/worlds/{scenario.World.Id}/storylines");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var storylines = await response.Content.ReadFromJsonAsync<List<ArtifactListItemResponse>>();
        Assert.That(storylines, Is.Not.Null);
        Assert.That(storylines!.Select(s => s.Id), Is.EquivalentTo(new[] { caravan.Id, prophecy.Id }));
        Assert.That(storylines!.All(s => s.Type == "Storyline"), Is.True);
    }

    [Test]
    public async Task List_FiltersByStatus()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Active Arc", type: ArtifactType.Storyline, status: ArtifactStatus.Active);
        var resolved = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Resolved Arc", type: ArtifactType.Storyline, status: ArtifactStatus.Resolved);

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/storylines?status=Resolved");

        var storylines = await response.Content.ReadFromJsonAsync<List<ArtifactListItemResponse>>();
        Assert.That(storylines!.Select(s => s.Id), Is.EqualTo(new[] { resolved.Id }));
    }

    [Test]
    public async Task List_InvalidStatus_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/storylines?status=NotAStatus");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task List_NonMember_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-story", email: "outsider-story@example.com", nickname: "Outsider");

        var response = await outsider.GetAsync($"/api/worlds/{scenario.World.Id}/storylines");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Continuity_NonGm_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.PlayerClient.GetAsync($"/api/worlds/{scenario.World.Id}/storylines/continuity");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Continuity_Gm_EmptyWorld_ReturnsZeroActive()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync($"/api/worlds/{scenario.World.Id}/storylines/continuity");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var report = await response.Content.ReadFromJsonAsync<StorylineContinuityResponse>();
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.ActiveCount, Is.EqualTo(0));
        Assert.That(report.Quiet, Is.Empty);
    }

    [Test]
    public async Task WrapUp_NonGm_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.PlayerClient.GetAsync($"/api/worlds/{scenario.World.Id}/storylines/wrap-up");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task WrapUp_Gm_EmptyWorld_HasNoWork()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync($"/api/worlds/{scenario.World.Id}/storylines/wrap-up");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var view = await response.Content.ReadFromJsonAsync<WrapUpResponse>();
        Assert.That(view!.HasWork, Is.False);
    }

    [Test]
    public async Task WrapUp_Gm_ClosureRoundTripsToResolvedStatus()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var arc = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Finished Arc", type: ArtifactType.Storyline, status: ArtifactStatus.Active);

        var apply = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/storylines/wrap-up",
            new { closures = new[] { new { storylineId = arc.Id, status = "Resolved" } } });

        Assert.That(apply.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await apply.Content.ReadFromJsonAsync<WrapUpApplyResponse>();
        Assert.That(result!.Closed, Is.EqualTo(1));
        Assert.That(result.BatchId, Is.Not.Null);

        // The storyline is now Resolved — the closure applied, not just proposed.
        var resolved = await scenario.GmClient.GetFromJsonAsync<List<ArtifactListItemResponse>>(
            $"/api/worlds/{scenario.World.Id}/storylines?status=Resolved");
        Assert.That(resolved!.Select(s => s.Id), Does.Contain(arc.Id));
    }

    [Test]
    public async Task WrapUp_Gm_InvalidClosureStatus_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var arc = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Arc", type: ArtifactType.Storyline);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/storylines/wrap-up",
            new { closures = new[] { new { storylineId = arc.Id, status = "Active" } } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
