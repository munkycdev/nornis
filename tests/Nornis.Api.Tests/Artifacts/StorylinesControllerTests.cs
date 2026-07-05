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
            _factory, scenario.Campaign.Id, "Captain Voss", type: ArtifactType.Character);
        var caravan = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "Missing Caravan", type: ArtifactType.Storyline);
        var prophecy = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "The Drowned Prophecy", type: ArtifactType.Storyline);

        var response = await scenario.GmClient.GetAsync($"/api/campaigns/{scenario.Campaign.Id}/storylines");

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
            _factory, scenario.Campaign.Id, "Active Arc", type: ArtifactType.Storyline, status: ArtifactStatus.Active);
        var resolved = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "Resolved Arc", type: ArtifactType.Storyline, status: ArtifactStatus.Resolved);

        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/storylines?status=Resolved");

        var storylines = await response.Content.ReadFromJsonAsync<List<ArtifactListItemResponse>>();
        Assert.That(storylines!.Select(s => s.Id), Is.EqualTo(new[] { resolved.Id }));
    }

    [Test]
    public async Task List_InvalidStatus_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/storylines?status=NotAStatus");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task List_NonMember_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-story", email: "outsider-story@example.com", nickname: "Outsider");

        var response = await outsider.GetAsync($"/api/campaigns/{scenario.Campaign.Id}/storylines");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
