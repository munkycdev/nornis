using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Canon;

[TestFixture]
public class CanonControllerTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task Get_AsGm_ReturnsFactsAndRelationships()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "Captain Voss");
        var harbor = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "Black Harbor", type: ArtifactType.Location);

        await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "denied", "knowing about the caravan", TruthState.Rumor);
        await KnowledgeTestHelpers.CreateTestRelationshipAsync(
            _factory, scenario.Campaign.Id, voss.Id, harbor.Id, "LocatedIn", TruthState.Confirmed);

        var response = await scenario.GmClient.GetAsync($"/api/campaigns/{scenario.Campaign.Id}/canon");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var entries = await response.Content.ReadFromJsonAsync<List<CanonEntryResponse>>();
        Assert.That(entries, Is.Not.Null);
        Assert.That(entries!, Has.Count.EqualTo(2));
        Assert.That(entries!.Select(e => e.Kind), Is.EquivalentTo(new[] { "Fact", "Relationship" }));
    }

    [Test]
    public async Task Get_AsPlayer_ExcludesGmOnlyAndHiddenEntries()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "Captain Voss");

        await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "public", "in Black Harbor", TruthState.Confirmed, VisibilityScope.PartyVisible);
        await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "secret", "is a smuggler", TruthState.Confirmed, VisibilityScope.GMOnly);
        // Party-visible scope but Hidden truth state — still GM-only.
        await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "truth", "is the traitor", TruthState.Hidden, VisibilityScope.PartyVisible);

        var response = await scenario.PlayerClient.GetAsync($"/api/campaigns/{scenario.Campaign.Id}/canon");

        var entries = await response.Content.ReadFromJsonAsync<List<CanonEntryResponse>>();
        Assert.That(entries!, Has.Count.EqualTo(1));
        Assert.That(entries![0].Label, Is.EqualTo("public"));
    }

    [Test]
    public async Task Get_TruthStateFilter_ReturnsOnlyMatching()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.Campaign.Id, "Captain Voss");
        await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "confirmed", "value", TruthState.Confirmed);
        await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, "rumor", "value", TruthState.Rumor);

        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/canon?truthState=Rumor");

        var entries = await response.Content.ReadFromJsonAsync<List<CanonEntryResponse>>();
        Assert.That(entries!, Has.Count.EqualTo(1));
        Assert.That(entries![0].Label, Is.EqualTo("rumor"));
        Assert.That(entries![0].TruthState, Is.EqualTo("Rumor"));
    }

    [Test]
    public async Task Get_InvalidTruthState_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/canon?truthState=NotAState");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Get_NonMember_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-canon", email: "outsider-canon@example.com", nickname: "Outsider");

        var response = await outsider.GetAsync($"/api/campaigns/{scenario.Campaign.Id}/canon");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
