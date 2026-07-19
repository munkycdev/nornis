using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// End-to-end invitation flow over real HTTP (provisioning + world-membership filter run for
/// real). The marquee case is a brand-new, never-provisioned user redeeming an invite and
/// ending up both provisioned and joined in a single request.
/// </summary>
[TestFixture]
public class InviteFlowTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Guid> CreateWorldAsGm(HttpClient gmClient)
    {
        var response = await gmClient.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Black Harbor Investigation", "A dark mystery", "D&D 5e"));
        response.EnsureSuccessStatusCode();
        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        return world!.Id;
    }

    private static async Task<WorldInviteResponse> CreateInvite(
        HttpClient gmClient, Guid worldId, string role = "Player", int? maxUses = null)
    {
        var response = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/invites",
            new CreateInviteRequest(role, null, maxUses));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorldInviteResponse>())!;
    }

    private HttpClient GmClient(string suffix) => _factory.CreateAuthenticatedClient(
        sub: $"auth0|gm-{suffix}", email: "voss@blackharbor.com", nickname: "Captain Voss");

    [Test]
    public async Task AcceptInvite_ByBrandNewUser_ProvisionsAccountAndJoinsWorld()
    {
        var gmClient = GmClient("invite-101");
        var worldId = await CreateWorldAsGm(gmClient);
        var invite = await CreateInvite(gmClient, worldId, role: "Player");

        // A user who has NEVER hit the API before redeems the invite.
        const string newcomerSub = "auth0|newcomer-101";
        var newcomer = _factory.CreateAuthenticatedClient(
            sub: newcomerSub, email: "newcomer@example.com", nickname: "Newcomer");

        var response = await newcomer.PostAsync($"/api/invites/{invite.Code}/accept", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<AcceptInviteResponse>();
        Assert.That(body!.WorldId, Is.EqualTo(worldId));
        Assert.That(body.AlreadyMember, Is.False);

        // The newcomer was provisioned AND joined with the invite's role, in one request.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.FirstAsync(u => u.Auth0SubjectId == newcomerSub);
        var member = await db.WorldMembers.FirstOrDefaultAsync(m => m.WorldId == worldId && m.UserId == user.Id);
        Assert.That(member, Is.Not.Null);
        Assert.That(member!.Role, Is.EqualTo(WorldRole.Player));
    }

    [Test]
    public async Task PreviewInvite_ReturnsWorldNameAndRole()
    {
        var gmClient = GmClient("invite-102");
        var worldId = await CreateWorldAsGm(gmClient);
        var invite = await CreateInvite(gmClient, worldId, role: "Observer");

        var viewer = _factory.CreateAuthenticatedClient(
            sub: "auth0|viewer-102", email: "viewer@example.com", nickname: "Viewer");

        var response = await viewer.GetAsync($"/api/invites/{invite.Code}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var preview = await response.Content.ReadFromJsonAsync<InvitePreviewResponse>();
        Assert.That(preview!.WorldName, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(preview.Role, Is.EqualTo("Observer"));
        Assert.That(preview.Status, Is.EqualTo("Active"));
    }

    [Test]
    public async Task AcceptInvite_WhenAlreadyMember_IsIdempotent()
    {
        var gmClient = GmClient("invite-103");
        var worldId = await CreateWorldAsGm(gmClient);
        var invite = await CreateInvite(gmClient, worldId);

        var newcomer = _factory.CreateAuthenticatedClient(
            sub: "auth0|newcomer-103", email: "newcomer@example.com", nickname: "Newcomer");

        var first = await newcomer.PostAsync($"/api/invites/{invite.Code}/accept", null);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = await newcomer.PostAsync($"/api/invites/{invite.Code}/accept", null);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await second.Content.ReadFromJsonAsync<AcceptInviteResponse>();
        Assert.That(body!.AlreadyMember, Is.True);
    }

    [Test]
    public async Task CreateInvite_ByNonGm_Returns403()
    {
        var gmClient = GmClient("invite-104");
        var worldId = await CreateWorldAsGm(gmClient);
        var invite = await CreateInvite(gmClient, worldId);

        // Join as a Player, then try to mint an invite.
        var player = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-104", email: "player@example.com", nickname: "Player");
        await player.PostAsync($"/api/invites/{invite.Code}/accept", null);

        var response = await player.PostAsJsonAsync($"/api/worlds/{worldId}/invites",
            new CreateInviteRequest("Player", null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AcceptInvite_AfterRevoke_Returns409()
    {
        var gmClient = GmClient("invite-105");
        var worldId = await CreateWorldAsGm(gmClient);
        var invite = await CreateInvite(gmClient, worldId);

        var revoke = await gmClient.DeleteAsync($"/api/worlds/{worldId}/invites/{invite.Id}");
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var newcomer = _factory.CreateAuthenticatedClient(
            sub: "auth0|newcomer-105", email: "newcomer@example.com", nickname: "Newcomer");
        var response = await newcomer.PostAsync($"/api/invites/{invite.Code}/accept", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AcceptInvite_WhenMaxUsesReached_Returns409()
    {
        var gmClient = GmClient("invite-106");
        var worldId = await CreateWorldAsGm(gmClient);
        var invite = await CreateInvite(gmClient, worldId, maxUses: 1);

        var first = _factory.CreateAuthenticatedClient(
            sub: "auth0|first-106", email: "first@example.com", nickname: "First");
        var firstResponse = await first.PostAsync($"/api/invites/{invite.Code}/accept", null);
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = _factory.CreateAuthenticatedClient(
            sub: "auth0|second-106", email: "second@example.com", nickname: "Second");
        var secondResponse = await second.PostAsync($"/api/invites/{invite.Code}/accept", null);
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AcceptInvite_UnknownCode_Returns404()
    {
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|wanderer-107", email: "wanderer@example.com", nickname: "Wanderer");

        var response = await client.PostAsync("/api/invites/does-not-exist/accept", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AcceptInvite_Unauthenticated_Returns401()
    {
        // No bearer token — the global fallback policy rejects the request.
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsync("/api/invites/whatever/accept", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
