using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Reveal;

/// <summary>
/// End-to-end proof of the reveal loop over real controller/auth/EF wiring: a GM promotes
/// GM-only knowledge and a player then sees it, non-GMs are forbidden, and an unclosed set
/// is rejected with the missing dependency.
/// </summary>
[TestFixture]
[Category("Feature: knowledge-reveal")]
public class RevealEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private string RevealUrl => $"/api/worlds/{_scenario.World.Id}/reveal";
    private string ArtifactsUrl => $"/api/worlds/{_scenario.World.Id}/artifacts";

    [Test]
    public async Task Reveal_Gm_PromotesGmOnlyArtifact_AndPlayerThenSeesIt()
    {
        var cove = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, _scenario.World.Id, name: "Smuggler's Cove",
            type: ArtifactType.Location, visibility: VisibilityScope.GMOnly);

        var before = await (await _scenario.PlayerClient.GetAsync(ArtifactsUrl)).Content.ReadAsStringAsync();
        Assert.That(before, Does.Not.Contain("Smuggler's Cove"), "GM-only artifact is hidden before the reveal");

        var response = await _scenario.GmClient.PostAsJsonAsync(RevealUrl, new { artifactIds = new[] { cove.Id } });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await (await _scenario.PlayerClient.GetAsync(ArtifactsUrl)).Content.ReadAsStringAsync();
        Assert.That(after, Does.Contain("Smuggler's Cove"), "player sees it once revealed");
    }

    [Test]
    public async Task Reveal_Player_IsForbidden()
    {
        var cove = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, _scenario.World.Id, name: "Smuggler's Cove",
            type: ArtifactType.Location, visibility: VisibilityScope.GMOnly);

        var response = await _scenario.PlayerClient.PostAsJsonAsync(RevealUrl, new { artifactIds = new[] { cove.Id } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Reveal_NotReferenceClosed_Returns422_WithTheMissingArtifact()
    {
        var cove = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, _scenario.World.Id, name: "Smuggler's Cove",
            type: ArtifactType.Location, visibility: VisibilityScope.GMOnly);
        var fact = await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, cove.Id, predicate: "controls", value: "the docks", visibility: VisibilityScope.GMOnly);

        var response = await _scenario.GmClient.PostAsJsonAsync(RevealUrl, new { factIds = new[] { fact.Id } });

        Assert.That((int)response.StatusCode, Is.EqualTo(422));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("reveal_not_closed"));
        Assert.That(body, Does.Contain(cove.Id.ToString()), "the missing parent artifact is named so the GM can add it");
    }

    [Test]
    public async Task Reveal_Gm_PromotesGmOnlyFact_AndPlayerThenSeesItsValue()
    {
        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, _scenario.World.Id, name: "Captain Voss",
            type: ArtifactType.Character, visibility: VisibilityScope.PartyVisible);
        var secret = await KnowledgeTestHelpers.CreateTestFactAsync(
            _factory, voss.Id, predicate: "true allegiance", value: "Smuggler king", visibility: VisibilityScope.GMOnly);

        var detailUrl = $"/api/worlds/{_scenario.World.Id}/artifacts/{voss.Id}";
        var before = await (await _scenario.PlayerClient.GetAsync(detailUrl)).Content.ReadAsStringAsync();
        Assert.That(before, Does.Not.Contain("Smuggler king"));

        var response = await _scenario.GmClient.PostAsJsonAsync(RevealUrl, new { factIds = new[] { secret.Id } });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await (await _scenario.PlayerClient.GetAsync(detailUrl)).Content.ReadAsStringAsync();
        Assert.That(after, Does.Contain("Smuggler king"), "the revealed fact is now on the player's view of Voss");
    }
}
