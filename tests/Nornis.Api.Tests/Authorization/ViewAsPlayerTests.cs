using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authorization;

/// <summary>
/// "View as player": a GM's client can send <c>X-Nornis-View-As: Player</c> and the
/// membership filters resolve them as a Player for that request — reads become
/// player-shaped and GM-gated endpoints fail closed with 403. The header can only ever
/// downgrade: non-GM senders and non-"Player" values are ignored.
/// Feature 20, Requirement 5.
/// </summary>
[TestFixture]
public class ViewAsPlayerTests
{
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

    private async Task<(HttpClient Client, Guid WorldId)> CreateGmWithWorldAsync()
    {
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-vespergale",
            email: "gm@vespergale.com",
            nickname: "GM Crane");

        var createResponse = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("The Vespergale Reach", "Demo", "D&D 5e"));
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var world = await createResponse.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world, Is.Not.Null);
        return (client, world!.Id);
    }

    [Test]
    public async Task Gm_WithViewAsHeader_IsResolvedAsPlayer()
    {
        var (client, worldId) = await CreateGmWithWorldAsync();
        client.DefaultRequestHeaders.Add(HttpContextExtensions.ViewAsHeaderName, "Player");

        var response = await client.GetAsync($"/api/worlds/{worldId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world!.MyRole, Is.EqualTo("Player"));
    }

    [Test]

    [Category("Authorization")]
    public async Task Gm_WithViewAsHeader_GmGatedWriteFailsClosed()
    {
        var (client, worldId) = await CreateGmWithWorldAsync();
        client.DefaultRequestHeaders.Add(HttpContextExtensions.ViewAsHeaderName, "Player");

        var response = await client.PutAsJsonAsync($"/api/worlds/{worldId}",
            new UpdateWorldRequest("Renamed", null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Gm_WithoutHeader_IsUnaffected()
    {
        var (client, worldId) = await CreateGmWithWorldAsync();

        var get = await client.GetAsync($"/api/worlds/{worldId}");
        var world = await get.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world!.MyRole, Is.EqualTo("GM"));

        var put = await client.PutAsJsonAsync($"/api/worlds/{worldId}",
            new UpdateWorldRequest("Renamed", null, null));
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Gm_WithNonPlayerHeaderValue_IsIgnored()
    {
        var (client, worldId) = await CreateGmWithWorldAsync();
        client.DefaultRequestHeaders.Add(HttpContextExtensions.ViewAsHeaderName, "Observer");

        var response = await client.GetAsync($"/api/worlds/{worldId}");

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world!.MyRole, Is.EqualTo("GM"));
    }

    // ------------------------------------------------ ApplyViewAs unit coverage --

    private static WorldMember Member(WorldRole role) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Role = role,
        DisplayName = "Someone",
        JoinedAt = DateTimeOffset.UtcNow,
    };

    private static HttpContext ContextWithHeader(string? value)
    {
        var context = new DefaultHttpContext();
        if (value is not null)
        {
            context.Request.Headers[HttpContextExtensions.ViewAsHeaderName] = value;
        }

        return context;
    }

    [Test]
    public void ApplyViewAs_GmWithPlayerHeader_ReturnsDetachedPlayerCopy()
    {
        var member = Member(WorldRole.GM);

        var result = ContextWithHeader("Player").ApplyViewAs(member);

        Assert.That(result, Is.Not.SameAs(member), "must not mutate the tracked entity");
        Assert.That(result.Role, Is.EqualTo(WorldRole.Player));
        Assert.That(member.Role, Is.EqualTo(WorldRole.GM), "original stays untouched");
        Assert.That(result.Id, Is.EqualTo(member.Id));
        Assert.That(result.WorldId, Is.EqualTo(member.WorldId));
        Assert.That(result.UserId, Is.EqualTo(member.UserId));
    }

    [Test]
    public void ApplyViewAs_HeaderValueIsCaseInsensitive()
    {
        var result = ContextWithHeader("player").ApplyViewAs(Member(WorldRole.GM));

        Assert.That(result.Role, Is.EqualTo(WorldRole.Player));
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public void ApplyViewAs_NonGmSender_IsIgnored(WorldRole role)
    {
        var member = Member(role);

        var result = ContextWithHeader("Player").ApplyViewAs(member);

        Assert.That(result, Is.SameAs(member));
        Assert.That(result.Role, Is.EqualTo(role));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("GM")]
    [TestCase("Observer")]
    public void ApplyViewAs_MissingOrNonPlayerValue_IsIgnored(string? value)
    {
        var member = Member(WorldRole.GM);

        var result = ContextWithHeader(value).ApplyViewAs(member);

        Assert.That(result, Is.SameAs(member));
        Assert.That(result.Role, Is.EqualTo(WorldRole.GM));
    }
}
