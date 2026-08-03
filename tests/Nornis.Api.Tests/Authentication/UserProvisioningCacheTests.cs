using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authentication;

/// <summary>
/// <c>UserProvisioningMiddleware</c> runs on every authenticated request and its only job is to
/// turn a JWT subject into a Guid, so its lookup was the most-executed query in the system. It is
/// now cached — which is only safe if the cache cannot serve one user's identity to another, and
/// cannot hand the same mutable entity to two concurrent requests.
/// </summary>
[TestFixture]
public class UserProvisioningCacheTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Guid> ResolvedUserIdAsync(string sub)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Auth0SubjectId == sub);
        return user.Id;
    }

    [Test]
    public async Task RepeatedRequests_ResolveTheSameUser_WithoutCreatingDuplicates()
    {
        var sub = "auth0|cache-same-user";
        var client = _factory.CreateAuthenticatedClient(sub: sub, email: "voss@blackharbor.test", nickname: "Voss");

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/api/worlds");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"request {i + 1} should succeed");
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var matches = await db.Users.AsNoTracking().CountAsync(u => u.Auth0SubjectId == sub);

        Assert.That(matches, Is.EqualTo(1), "a cache hit must not re-provision the user");
    }

    [Test]
    public async Task DifferentSubjects_NeverShareACacheEntry()
    {
        // The failure this guards against is the worst one available here: serving user A's
        // identity to user B, which would hand B every world A can see.
        var clientA = _factory.CreateAuthenticatedClient(
            sub: "auth0|cache-user-a", email: "a@blackharbor.test", nickname: "A");
        var clientB = _factory.CreateAuthenticatedClient(
            sub: "auth0|cache-user-b", email: "b@blackharbor.test", nickname: "B");

        // Each creates a world, then reads its own list back. If the cache leaked, one would see
        // the other's world.
        var createA = await clientA.PostAsJsonAsync("/api/worlds", new { name = "A's world" });
        var createB = await clientB.PostAsJsonAsync("/api/worlds", new { name = "B's world" });
        Assert.That(createA.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(createB.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var idA = await ResolvedUserIdAsync("auth0|cache-user-a");
        var idB = await ResolvedUserIdAsync("auth0|cache-user-b");
        Assert.That(idA, Is.Not.EqualTo(idB));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var worldsOfA = await db.WorldMembers.AsNoTracking().CountAsync(m => m.UserId == idA);
        var worldsOfB = await db.WorldMembers.AsNoTracking().CountAsync(m => m.UserId == idB);

        Assert.Multiple(() =>
        {
            Assert.That(worldsOfA, Is.EqualTo(1), "A must own exactly the world A created");
            Assert.That(worldsOfB, Is.EqualTo(1), "B must own exactly the world B created");
        });
    }

    [Test]

    [Category("Authorization")]
    public async Task DownstreamFailure_OnAWarmCache_IsNotSwallowedByTheMiddleware()
    {
        // The middleware catches exceptions around user *resolution*. If the continuation ran
        // inside that try, a warm cache would put the whole application inside those catch
        // clauses — a controller's DbUpdateException would be caught here and the request
        // re-executed from routing, and any downstream 500 would be reported as a 503 blamed on
        // user provisioning. This asserts a downstream failure is neither of those things.
        using var factory = new NornisWebApplicationFactory();
        var client = factory.CreateAuthenticatedClient(
            sub: "auth0|cache-downstream", email: "d@blackharbor.test", nickname: "D");

        // Warm the cache.
        Assert.That((await client.GetAsync("/api/worlds")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // A malformed world id on a world-scoped route reaches routing and the filter with the
        // cache already warm — the path that would previously have been wrapped.
        var response = await client.GetAsync($"/api/worlds/{Guid.NewGuid()}/sources/activity");

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable),
            "a downstream outcome must never be reported as a user-provisioning failure");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "a non-member should get the filter's 403, unchanged by caching");
    }

    [Test]
    public async Task WriteThatViolatesAUniqueIndex_OnAWarmCache_IsNotRetried()
    {
        // The concrete version of the same hazard: a DbUpdateException from a controller used to
        // land in the middleware's DbUpdateException handler, which re-resolves the user and falls
        // through to a second _next — running the whole request, and its side effects, twice.
        using var factory = new NornisWebApplicationFactory();
        var client = factory.CreateAuthenticatedClient(
            sub: "auth0|cache-duplicate", email: "dup@blackharbor.test", nickname: "Dup");

        var created = await client.PostAsJsonAsync("/api/worlds", new { name = "Duplicate probe" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Cache is warm now. Create several worlds with the same name — legal, so each should
        // produce exactly one world. A re-executed request would produce more than we asked for.
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/worlds", new { name = "Repeat" });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var repeats = await db.Worlds.AsNoTracking().CountAsync(w => w.Name == "Repeat");

        Assert.That(repeats, Is.EqualTo(3), "each request must create exactly one world, once");
    }

    [Test]
    public async Task InterleavedRequestsFromTwoUsers_StayDistinct()
    {
        // Alternating hits exercise the cache in the order most likely to expose a shared key or
        // a shared entity instance.
        var clientA = _factory.CreateAuthenticatedClient(
            sub: "auth0|cache-interleave-a", email: "ia@blackharbor.test", nickname: "IA");
        var clientB = _factory.CreateAuthenticatedClient(
            sub: "auth0|cache-interleave-b", email: "ib@blackharbor.test", nickname: "IB");

        for (var i = 0; i < 4; i++)
        {
            Assert.That((await clientA.GetAsync("/api/worlds")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await clientB.GetAsync("/api/worlds")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var subs = await db.Users.AsNoTracking()
            .Where(u => u.Auth0SubjectId.StartsWith("auth0|cache-interleave-"))
            .Select(u => u.Auth0SubjectId)
            .ToListAsync();

        Assert.That(subs, Is.EquivalentTo(["auth0|cache-interleave-a", "auth0|cache-interleave-b"]));
    }
}
