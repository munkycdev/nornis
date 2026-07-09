using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Artifacts;

[TestFixture]
public class ArtifactsControllerTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    #region List

    [Test]
    public async Task List_AsPlayer_ExcludesGmOnlyArtifacts()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Captain Voss", visibility: VisibilityScope.PartyVisible);
        await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Hidden Ledger", visibility: VisibilityScope.GMOnly);

        var response = await scenario.PlayerClient.GetAsync($"/api/worlds/{scenario.World.Id}/artifacts");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var artifacts = await response.Content.ReadFromJsonAsync<List<ArtifactListItemResponse>>();
        Assert.That(artifacts, Is.Not.Null);
        Assert.That(artifacts!.Select(a => a.Id), Is.EqualTo(new[] { voss.Id }));
    }

    [Test]
    public async Task List_FiltersByType()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Captain Voss", type: ArtifactType.Character);
        var caravan = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Missing Caravan", type: ArtifactType.Storyline);

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/artifacts?type=Storyline");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var artifacts = await response.Content.ReadFromJsonAsync<List<ArtifactListItemResponse>>();
        Assert.That(artifacts!.Select(a => a.Id), Is.EqualTo(new[] { caravan.Id }));
        Assert.That(artifacts![0].Type, Is.EqualTo("Storyline"));
    }

    [Test]
    public async Task List_FiltersByStatus()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Active Plot", status: ArtifactStatus.Active);
        var resolved = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Resolved Plot", status: ArtifactStatus.Resolved);

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/artifacts?status=Resolved");

        var artifacts = await response.Content.ReadFromJsonAsync<List<ArtifactListItemResponse>>();
        Assert.That(artifacts!.Select(a => a.Id), Is.EqualTo(new[] { resolved.Id }));
    }

    [Test]
    public async Task List_InvalidType_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/artifacts?type=NotAType");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task List_NonMember_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider", email: "outsider@example.com", nickname: "Outsider");

        var response = await outsider.GetAsync($"/api/worlds/{scenario.World.Id}/artifacts");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region Detail

    [Test]
    public async Task GetById_ReturnsArtifactWithFactsRelationshipsConnectedAndSources()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Captain Voss");
        var harbor = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Black Harbor", type: ArtifactType.Location);

        var fact = await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "denied", "knowing about the caravan", TruthState.Rumor);
        var rel = await KnowledgeTestHelpers.CreateTestRelationshipAsync(
            _factory, scenario.World.Id, voss.Id, harbor.Id, "LocatedIn");

        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId);
        await KnowledgeTestHelpers.CreateTestSourceReferenceAsync(
            _factory, source.Id, SourceReferenceTargetType.ArtifactFact, fact.Id, quote: "He denied it.");

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/artifacts/{voss.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await response.Content.ReadFromJsonAsync<ArtifactDetailResponse>();
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Name, Is.EqualTo("Captain Voss"));
        Assert.That(detail.Facts.Select(f => f.Id), Is.EqualTo(new[] { fact.Id }));
        Assert.That(detail.Relationships.Select(r => r.Id), Is.EqualTo(new[] { rel.Id }));
        Assert.That(detail.ConnectedArtifacts.Select(c => c.Name), Does.Contain("Black Harbor"));
        Assert.That(detail.SourceReferences.Select(s => s.TargetId), Does.Contain(fact.Id));
    }

    [Test]
    public async Task GetById_Missing_ReturnsNotFound()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/artifacts/{Guid.NewGuid()}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetById_GmOnlyArtifact_AsPlayer_ReturnsNotFound()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var hidden = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Hidden Ledger", visibility: VisibilityScope.GMOnly);

        var response = await scenario.PlayerClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/artifacts/{hidden.Id}");

        // Not-found (not forbidden) so a Player cannot even confirm the artifact exists.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion
}
