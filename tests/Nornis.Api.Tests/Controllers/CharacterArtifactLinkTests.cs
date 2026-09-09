using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

[TestFixture]
public class CharacterArtifactLinkTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private static async Task<CharacterResponse> CreateCharacterAsync(HttpClient client, Guid worldId, string name = "Tavrin")
    {
        var response = await client.PostAsJsonAsync($"/api/worlds/{worldId}/characters", new { name });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<CharacterResponse>())!;
    }

    [Test]
    public async Task Update_LinksCharacterToArtifact_AndResponseCarriesIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.PlayerClient, scenario.World.Id);
        var artifact = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Tavrin (record)", type: ArtifactType.Character);

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}",
            new { artifactId = artifact.Id });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await response.Content.ReadFromJsonAsync<CharacterResponse>();
        Assert.That(updated!.ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task Update_LinkToWrongTypeArtifact_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.PlayerClient, scenario.World.Id);
        var artifact = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Black Harbor", type: ArtifactType.Location);

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}",
            new { artifactId = artifact.Id });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Update_UnlinkFlag_ClearsTheLink()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.PlayerClient, scenario.World.Id);
        var artifact = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Tavrin (record)", type: ArtifactType.Character);

        var linked = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}",
            new { artifactId = artifact.Id });
        Assert.That(linked.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}",
            new { unlinkArtifact = true });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await response.Content.ReadFromJsonAsync<CharacterResponse>();
        Assert.That(updated!.ArtifactId, Is.Null);
    }

    [Test]
    public async Task Claim_PlayerTakesOverGmCharacter()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.GmClient, scenario.World.Id, "Orphaned Hero");

        var response = await scenario.PlayerClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}/claim", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var claimed = await response.Content.ReadFromJsonAsync<CharacterResponse>();
        Assert.That(claimed!.WorldMemberId, Is.Not.EqualTo(character.WorldMemberId));

        // The player's mine=true list now includes it.
        var mine = await scenario.PlayerClient.GetFromJsonAsync<List<CharacterResponse>>(
            $"/api/worlds/{scenario.World.Id}/characters?mine=true");
        Assert.That(mine!.Select(c => c.Id), Does.Contain(character.Id));
    }

    [Test]

    [Category("Authorization")]
    public async Task Claim_AsObserver_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.GmClient, scenario.World.Id, "Untouchable");

        var response = await scenario.ObserverClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}/claim", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]

    [Category("Authorization")]
    public async Task Update_PlayerLinksGmOnlyArtifact_ReturnsBadRequest()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.PlayerClient, scenario.World.Id);
        var artifact = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Secret Twin", type: ArtifactType.Character,
            visibility: VisibilityScope.GMOnly);

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/characters/{character.Id}",
            new { artifactId = artifact.Id });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// A GM links a player's character to a GM-only artifact. On the wire, every character
    /// endpoint must then read to the player exactly as an unlinked character does — the list,
    /// the single read, the dossier envelope, and the response to the player's own edit. The
    /// GM still sees the link. This is the property the live check found missing in the JSON
    /// while the page had it right.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task HiddenLink_IsAbsentFromEveryCharacterResponse_ForThePlayer()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var character = await CreateCharacterAsync(scenario.PlayerClient, scenario.World.Id);
        var artifact = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Secret Twin", type: ArtifactType.Character,
            visibility: VisibilityScope.GMOnly);
        var basePath = $"/api/worlds/{scenario.World.Id}/characters";

        var linked = await scenario.GmClient.PutAsJsonAsync(
            $"{basePath}/{character.Id}", new { artifactId = artifact.Id });
        Assert.That(linked.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await linked.Content.ReadFromJsonAsync<CharacterResponse>())!.ArtifactId, Is.EqualTo(artifact.Id),
            "the GM who made the link sees it");

        var listed = await scenario.PlayerClient.GetFromJsonAsync<List<CharacterResponse>>(basePath);
        var single = await scenario.PlayerClient.GetFromJsonAsync<CharacterResponse>($"{basePath}/{character.Id}");
        var dossier = await scenario.PlayerClient.GetFromJsonAsync<CharacterDossierResponse>($"{basePath}/{character.Id}/dossier");
        var renamed = await scenario.PlayerClient.PutAsJsonAsync(
            $"{basePath}/{character.Id}", new { name = "Tavrin Ashgrave" });
        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var ownEdit = await renamed.Content.ReadFromJsonAsync<CharacterResponse>();
        var asGm = await scenario.GmClient.GetFromJsonAsync<CharacterResponse>($"{basePath}/{character.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(listed!.Single(c => c.Id == character.Id).ArtifactId, Is.Null, "list");
            Assert.That(single!.ArtifactId, Is.Null, "single read");
            Assert.That(dossier!.Character.ArtifactId, Is.Null, "dossier envelope");
            Assert.That(dossier.Record, Is.Null, "dossier record");
            Assert.That(ownEdit!.ArtifactId, Is.Null, "own edit");
            Assert.That(asGm!.ArtifactId, Is.EqualTo(artifact.Id), "GM read");
        });
    }
}
