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
/// <c>GET /api/worlds/{worldId}/members/addable</c> replaced <c>GET /api/users</c>.
///
/// <para>The old endpoint returned every username and id in the system to <b>any</b> authenticated
/// caller — it had no role check and no cap, and the only gate was the browser deciding not to
/// render the picker for non-GMs. Anyone with a token could enumerate the whole directory. These
/// tests are about the gate being on the server now: a non-member cannot reach it, a Player member
/// cannot either, and a GM gets only their own world's candidates, capped.</para>
/// </summary>
[TestFixture]
public class AddableUsersTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    // A search term is required and must be at least two characters, so the helper supplies one
    // that matches the users these tests seed ("Tavrin") unless a test is about the term itself.
    private static string Addable(Guid worldId, string q = "av") =>
        $"/api/worlds/{worldId}/members/addable?q={Uri.EscapeDataString(q)}";

    private async Task<Guid> CreateWorldAsGm(HttpClient gmClient)
    {
        var response = await gmClient.PostAsJsonAsync(
            "/api/worlds", new CreateWorldRequest("Black Harbor Investigation", "A dark mystery", "D&D 5e"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorldResponse>())!.Id;
    }

    private async Task<Guid> ProvisionUserAndGetId(string sub, string email, string? nickname = null)
    {
        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);
        await client.GetAsync("/api/worlds");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.FirstAsync(u => u.Auth0SubjectId == sub);
        return user.Id;
    }

    private HttpClient GmClient() => _factory.CreateAuthenticatedClient(
        sub: "auth0|gm-voss-001", email: "voss@blackharbor.com", nickname: "Captain Voss");

    // ------------------------------------------------------------------ the gate

    [Test]

    [Category("Authorization")]
    public async Task ANonMember_CannotEnumerateTheDirectory()
    {
        // The exposure the old endpoint had, stated as a test: an authenticated stranger asking
        // for users. It must not matter that they hold a valid token.
        var worldId = await CreateWorldAsGm(GmClient());
        var stranger = _factory.CreateAuthenticatedClient(
            sub: "auth0|stranger-001", email: "stranger@example.com", nickname: "Passing Stranger");

        var response = await stranger.GetAsync(Addable(worldId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]

    [Category("Authorization")]
    public async Task APlayerMember_CannotEnumerateTheDirectory()
    {
        // Being in the world is not enough. Adding members is GM work, so listing the candidates
        // is too — a Player has no reason to learn who else has an account.
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);
        var playerId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-001", "tavrin@example.com", "Tavrin");
        (await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members",
            new AddWorldMemberRequest(playerId, "Player"))).EnsureSuccessStatusCode();

        var playerClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-tavrin-001", email: "tavrin@example.com", nickname: "Tavrin");

        var response = await playerClient.GetAsync(Addable(worldId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]

    [Category("Authorization")]
    public async Task AGmOfAnotherWorld_CannotUseTheirRoleHere()
    {
        // Being a GM somewhere does not make you a GM everywhere. This is the membership filter's
        // job rather than the role check's, so it is worth pinning separately.
        var worldId = await CreateWorldAsGm(GmClient());
        var otherGm = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-other-001", email: "othergm@example.com", nickname: "Other GM");
        await CreateWorldAsGm(otherGm);

        var response = await otherGm.GetAsync(Addable(worldId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase("")]
    [TestCase("a")]
    [TestCase(" a ")]
    public async Task AShortOrEmptyTermIsRejectedRatherThanTreatedAsListEveryone(string q)
    {
        // The role check alone does not protect the directory: anyone can create a world and be
        // its GM, so "GM of this world" is self-issuable. What stops the table being paged out is
        // that there is no listing mode to reach — every call has to name someone.
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);
        await ProvisionUserAndGetId("auth0|player-tavrin-001", "tavrin@example.com", "Tavrin");

        var response = await gmClient.GetAsync(
            $"/api/worlds/{worldId}/members/addable?q={Uri.EscapeDataString(q)}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "a one-character term walks the alphabet in 26 requests");
    }

    [Test]
    public async Task AMissingTermIsRejectedToo()
    {
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);

        var response = await gmClient.GetAsync($"/api/worlds/{worldId}/members/addable");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task TheOldUnscopedEndpointIsGone()
    {
        // Removing the route is the actual fix; everything else is shape. If it ever comes back,
        // the gate above is decoration.
        var response = await GmClient().GetAsync("/api/users");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // ------------------------------------------------------------------ what a GM gets

    [Test]
    public async Task AGmSeesUsersWhoAreNotYetMembers()
    {
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);
        var outsiderId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-001", "tavrin@example.com", "Tavrin");

        var users = await gmClient.GetFromJsonAsync<List<UserSummaryResponse>>(Addable(worldId));

        Assert.That(users!.Select(u => u.Id), Does.Contain(outsiderId));
    }

    [Test]
    public async Task SomeoneAlreadyAddedDropsOutOfTheList()
    {
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);
        var playerId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-001", "tavrin@example.com", "Tavrin");

        var before = await gmClient.GetFromJsonAsync<List<UserSummaryResponse>>(Addable(worldId));
        Assert.That(before!.Select(u => u.Id), Does.Contain(playerId), "arrangement check");

        (await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members",
            new AddWorldMemberRequest(playerId, "Player"))).EnsureSuccessStatusCode();

        var after = await gmClient.GetFromJsonAsync<List<UserSummaryResponse>>(Addable(worldId));

        Assert.That(after!.Select(u => u.Id), Does.Not.Contain(playerId),
            "the picker must not offer to add someone who is already a member");
    }

    [Test]
    public async Task TheSearchTermNarrowsTheList()
    {
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);
        var tavrinId = await ProvisionUserAndGetId("auth0|player-tavrin-001", "tavrin@example.com", "Tavrin");
        await ProvisionUserAndGetId("auth0|player-mira-001", "mira@example.com", "Mira Kell");

        var users = await gmClient.GetFromJsonAsync<List<UserSummaryResponse>>(Addable(worldId, "avri"));

        Assert.Multiple(() =>
        {
            Assert.That(users!.Select(u => u.Id), Does.Contain(tavrinId));
            Assert.That(users!, Has.Count.EqualTo(1), "a search that returns everyone is not a search");
        });
    }

    [Test]
    public async Task TheResponseCarriesTheUsernameAndNothingElse()
    {
        // The old endpoint's one saving grace was that it projected to id + username. Widening
        // this to the entity would hand every GM an email address per row.
        //
        // Note what this deliberately does NOT assert: that the body contains no "auth0|". User
        // provisioning falls back to the raw subject when a token carries no nickname, so a
        // username legitimately CAN be an Auth0 subject — an assertion about the string would
        // pass here only because this fixture always supplies a nickname. That is a provisioning
        // concern, not this endpoint's, and pretending to cover it here would be worse than
        // leaving it uncovered.
        var gmClient = GmClient();
        var worldId = await CreateWorldAsGm(gmClient);
        await ProvisionUserAndGetId("auth0|player-tavrin-001", "tavrin@example.com", "Tavrin");

        var body = await gmClient.GetStringAsync(Addable(worldId, "Tavrin"));
        var rows = await gmClient.GetFromJsonAsync<List<UserSummaryResponse>>(Addable(worldId, "Tavrin"));

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain("tavrin@example.com"), "no email may reach the picker");
            Assert.That(body, Does.Contain("Tavrin"), "the username is what the picker needs");
            Assert.That(typeof(UserSummaryResponse).GetProperties().Select(p => p.Name),
                Is.EquivalentTo([nameof(UserSummaryResponse.Id), nameof(UserSummaryResponse.Username)]),
                "widening this contract is what would leak; the shape is the guard");
            Assert.That(rows, Is.Not.Empty, "the arrangement only means something if a row came back");
        });
    }
}
