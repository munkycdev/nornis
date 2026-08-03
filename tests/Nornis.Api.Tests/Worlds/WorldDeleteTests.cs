using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// End-to-end coverage for DELETE /api/worlds/{worldId}: GM-only, requires the typed
/// world name to match exactly, wipes the world's rows, and clears its blob prefix.
/// </summary>
[TestFixture]
public class WorldDeleteTests
{
    private const string WorldName = "Black Harbor Investigation";

    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    private async Task<Guid> CreateWorldAsGm(HttpClient gmClient)
    {
        var response = await gmClient.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(WorldName, "A dark mystery", "D&D 5e"));
        response.EnsureSuccessStatusCode();

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        return world!.Id;
    }

    private static string DeleteUrl(Guid worldId, string confirmName) =>
        $"/api/worlds/{worldId}?confirmName={Uri.EscapeDataString(confirmName)}";

    [Test]
    public async Task DeleteWorld_AsGmWithExactName_Returns204AndWipesTheWorld()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-del1", email: "voss@blackharbor.com", nickname: "Captain Voss");
        var worldId = await CreateWorldAsGm(gmClient);

        // Give the world some content so the wipe has something to chew on.
        var campaign = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/campaigns",
            new CreateCampaignRequest("Season One"));
        campaign.EnsureSuccessStatusCode();

        // A stray blob under the world's prefix must be removed with it.
        _factory.BlobStorage.Blobs[$"worlds/{worldId}/library/{Guid.NewGuid()}/notes.pdf"] =
            ([1, 2, 3], "application/pdf");

        var response = await gmClient.DeleteAsync(DeleteUrl(worldId, WorldName));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var list = await gmClient.GetFromJsonAsync<List<WorldListItemResponse>>("/api/worlds");
        Assert.That(list, Is.Empty);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(await db.Worlds.CountAsync(), Is.Zero);
        Assert.That(await db.WorldMembers.CountAsync(), Is.Zero);
        Assert.That(await db.Campaigns.CountAsync(), Is.Zero);

        Assert.That(_factory.BlobStorage.DeletedPrefixes, Is.EqualTo([$"worlds/{worldId}/"]));
        Assert.That(_factory.BlobStorage.Blobs, Is.Empty);
    }

    [Test]
    public async Task DeleteWorld_WithWrongName_Returns400AndKeepsTheWorld()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-del2", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var response = await gmClient.DeleteAsync(DeleteUrl(worldId, "black harbor investigation"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("confirmation_mismatch"));

        var stillThere = await gmClient.GetAsync($"/api/worlds/{worldId}");
        Assert.That(stillThere.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DeleteWorld_WithoutConfirmName_Returns400()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-del3", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var response = await gmClient.DeleteAsync($"/api/worlds/{worldId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]

    [Category("Authorization")]
    public async Task DeleteWorld_AsPlayer_Returns403()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-del4", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var playerClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-tavrin-del4", email: "tavrin@example.com", nickname: "Tavrin");
        await playerClient.GetAsync("/api/worlds"); // provision the user

        Guid playerUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            playerUserId = (await db.Users.FirstAsync(u => u.Auth0SubjectId == "auth0|player-tavrin-del4")).Id;
        }

        var add = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members",
            new AddWorldMemberRequest(playerUserId, "Player"));
        add.EnsureSuccessStatusCode();

        var response = await playerClient.DeleteAsync(DeleteUrl(worldId, WorldName));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var stillThere = await gmClient.GetAsync($"/api/worlds/{worldId}");
        Assert.That(stillThere.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]

    [Category("Authorization")]
    public async Task DeleteWorld_AsNonMember_Returns403()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-del5", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-del5", email: "outsider@example.com");

        var response = await outsider.DeleteAsync(DeleteUrl(worldId, WorldName));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
