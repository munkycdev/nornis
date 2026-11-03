using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authentication;

/// <summary>
/// Several sign-ins per account (feature 33), end to end through provisioning: a linked subject
/// opens the same account, the claim never provisions, and a removed one stops resolving.
/// </summary>
[TestFixture]
[Category("Authorization")]
public class SignInsEndpointTests
{
    private const string VossDiscord = "oauth2|discord|voss-signins";
    private const string VossGoogle = "google-oauth2|voss-signins";

    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task A_linked_sign_in_opens_the_same_account()
    {
        var discord = _factory.CreateAuthenticatedClient(sub: VossDiscord, email: "voss@blackharbor.test", nickname: "Voss");
        var created = await discord.PostAsJsonAsync("/api/worlds", new { Name = "Black Harbor" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var claim = await ClaimAsync(await LinkCodeAsync(discord), VossGoogle);

        Assert.That(claim.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await claim.Content.ReadFromJsonAsync<SignInClaimResponse>())!.Outcome, Is.EqualTo("linked"));
        var google = _factory.CreateAuthenticatedClient(sub: VossGoogle, email: "voss@gmail.test");
        var worlds = await (await google.GetAsync("/api/worlds")).Content.ReadAsStringAsync();
        Assert.That(worlds, Does.Contain("Black Harbor"));
        Assert.That(await DbAsync(db => db.Users.CountAsync()), Is.EqualTo(1), "no second user for the Google sign-in");
    }

    [Test]
    public async Task The_list_marks_the_sign_in_the_request_uses()
    {
        var discord = _factory.CreateAuthenticatedClient(sub: VossDiscord);
        await ClaimAsync(await LinkCodeAsync(discord), VossGoogle);

        var list = await _factory.CreateAuthenticatedClient(sub: VossGoogle)
            .GetFromJsonAsync<List<SignInResponse>>("/api/me/sign-ins");

        Assert.That(list!.Select(s => (s.Provider, s.Current)), Is.EquivalentTo([("Discord", false), ("Google", true)]));
    }

    [Test]
    public async Task A_refused_claim_creates_no_user_for_its_subject()
    {
        var response = await ClaimAsync("no-such-code", VossGoogle);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await DbAsync(db => db.UserSignIns.AnyAsync(s => s.Subject == VossGoogle)), Is.False);
        Assert.That(await DbAsync(db => db.Users.AnyAsync()), Is.False);
    }

    [Test]
    public async Task A_subject_with_its_own_account_is_refused()
    {
        var discord = _factory.CreateAuthenticatedClient(sub: VossDiscord);
        await discord.GetAsync("/api/worlds");
        await _factory.CreateAuthenticatedClient(sub: VossGoogle).GetAsync("/api/worlds"); // its own account

        var response = await ClaimAsync(await LinkCodeAsync(discord), VossGoogle);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await DbAsync(db => db.Users.CountAsync()), Is.EqualTo(2));
    }

    [Test]
    public async Task A_removed_sign_in_stops_opening_the_account_at_once()
    {
        var discord = _factory.CreateAuthenticatedClient(sub: VossDiscord);
        await discord.PostAsJsonAsync("/api/worlds", new { Name = "Black Harbor" });
        await ClaimAsync(await LinkCodeAsync(discord), VossGoogle);
        var google = _factory.CreateAuthenticatedClient(sub: VossGoogle);
        Assert.That(await (await google.GetAsync("/api/worlds")).Content.ReadAsStringAsync(), Does.Contain("Black Harbor"),
            "the Google subject is cached against the account now");
        var googleId = (await discord.GetFromJsonAsync<List<SignInResponse>>("/api/me/sign-ins"))!.Single(s => s.Provider == "Google").Id;

        var removed = await discord.DeleteAsync($"/api/me/sign-ins/{googleId}");

        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await (await google.GetAsync("/api/worlds")).Content.ReadAsStringAsync(), Does.Not.Contain("Black Harbor"),
            "the cache entry was evicted, so the subject is a stranger again");
    }

    [Test]
    public async Task The_sign_in_in_use_cannot_be_removed()
    {
        var discord = _factory.CreateAuthenticatedClient(sub: VossDiscord);
        await ClaimAsync(await LinkCodeAsync(discord), VossGoogle);
        var discordId = (await discord.GetFromJsonAsync<List<SignInResponse>>("/api/me/sign-ins"))!.Single(s => s.Current).Id;

        var response = await discord.DeleteAsync($"/api/me/sign-ins/{discordId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Bibliotaph_reaches_the_account_through_a_linked_sign_in()
    {
        var discord = _factory.CreateAuthenticatedClient(sub: VossDiscord, nickname: "Voss");
        await ClaimAsync(await LinkCodeAsync(discord), VossGoogle);
        var accountId = await DbAsync(db => db.UserSignIns.Where(s => s.Subject == VossDiscord).Select(s => s.UserId).SingleAsync());

        var bibliotaph = _factory.CreateClient();
        bibliotaph.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.GenerateBibliotaphToken(VossGoogle));
        var me = await bibliotaph.GetFromJsonAsync<BibliotaphMeResponse>("/api/bibliotaph/v1/me");

        Assert.That(me!.UserId, Is.EqualTo(accountId));
    }

    private static async Task<string> LinkCodeAsync(HttpClient session)
    {
        var response = await session.PostAsync("/api/me/sign-ins/link", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<SignInLinkResponse>())!.Code;
    }

    /// <summary>The claim as the Web makes it: with the new sign-in's own token.</summary>
    private Task<HttpResponseMessage> ClaimAsync(string code, string subject) =>
        _factory.CreateAuthenticatedClient(sub: subject).PostAsJsonAsync("/api/me/sign-ins/claim", new { Code = code });

    private async Task<T> DbAsync<T>(Func<NornisDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<NornisDbContext>());
    }
}
