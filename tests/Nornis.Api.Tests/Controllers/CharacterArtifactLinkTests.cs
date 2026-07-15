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
}
