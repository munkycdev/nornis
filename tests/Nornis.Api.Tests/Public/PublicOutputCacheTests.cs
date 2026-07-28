using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Public;

/// <summary>
/// Output caching on the anonymous public pages.
///
/// <para>Caching a response is the easiest way to serve the wrong person the wrong thing, so these
/// lead with the ways it could: one world's page answering for another's slug, a 404 for a slug
/// that does not exist yet being remembered after the GM enables it, and a world whose public
/// access was switched off carrying on regardless. The speed is incidental; the containment is the
/// point.</para>
/// </summary>
[TestFixture]
public class PublicOutputCacheTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private async Task<SourceTestScenario> SetupPublicWorldAsync(string slug)
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        await SetPublicAsync(scenario, slug, enabled: true);
        return scenario;
    }

    private static async Task SetPublicAsync(SourceTestScenario scenario, string? slug, bool enabled)
    {
        var update = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}",
            new UpdateWorldRequest(PublicSlug: slug, PublicAccessEnabled: enabled));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Stands in for the worker finishing with the note. Sources created through POST land at
    /// Draft, and a draft never reaches the public page whatever its visibility says.
    /// </summary>
    private async Task MarkProcessedAsync(Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        context.Sources.Single(s => s.Id == sourceId).ProcessingStatus = SourceProcessingStatus.Processed;
        await context.SaveChangesAsync();
    }

    private Task<PublicWorldResponse?> GetWorldAsync(string slug) =>
        _anonymous.GetFromJsonAsync<PublicWorldResponse>($"/api/public/worlds/{slug}");

    // ------------------------------------------------------------------ containment

    [Test]
    public async Task TwoWorldsDoNotShareACacheEntry()
    {
        // The failure that would make this change unshippable: a cache key that ignored the slug
        // would serve whichever world was asked for first to everyone.
        var first = await SetupPublicWorldAsync("black-harbor");

        // Named apart deliberately: the scenario helper gives every world the same name, and
        // comparing two identical names would prove nothing about which world answered.
        var second = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var rename = await second.GmClient.PutAsJsonAsync($"/api/worlds/{second.World.Id}",
            new UpdateWorldRequest(Name: "Vespergale Reach", PublicSlug: "vespergale-reach",
                PublicAccessEnabled: true));
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.OK), await rename.Content.ReadAsStringAsync());

        var a = await GetWorldAsync("black-harbor");
        var b = await GetWorldAsync("vespergale-reach");

        Assert.Multiple(() =>
        {
            Assert.That(a!.Slug, Is.EqualTo("black-harbor"));
            Assert.That(b!.Slug, Is.EqualTo("vespergale-reach"));
            Assert.That(a.Name, Is.Not.EqualTo(b.Name), "different worlds must not answer alike");
            Assert.That(first.World.Id, Is.Not.EqualTo(second.World.Id), "arrangement check");
        });
    }

    // Deliberately no test that an unknown slug's 404 is not remembered. It is not remembered —
    // the framework's default policy refuses to store any non-200 — but that is a framework
    // invariant this code cannot reach, so a test for it passes no matter what we do here. It read
    // like coverage and was worth nothing.

    [Test]
    public async Task TurningPublicAccessOff_TakesEffectImmediately()
    {
        // The kill switch. A cache that outlived it would keep serving a world the GM has just
        // withdrawn, which is the one staleness that is not merely cosmetic.
        var scenario = await SetupPublicWorldAsync("black-harbor");

        var live = await _anonymous.GetAsync("/api/public/worlds/black-harbor");
        Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK), "arrangement check");

        await SetPublicAsync(scenario, slug: null, enabled: false);

        var afterKill = await _anonymous.GetAsync("/api/public/worlds/black-harbor");

        Assert.That(afterKill.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "switching public access off must stop the world being served, not schedule it");
    }

    [Test]
    public async Task ChangingTheSlug_StopsTheOldLinkWorking()
    {
        // The half that a per-slug eviction would get wrong: entries under the OLD slug are the
        // dangerous ones, because that link no longer addresses this world.
        var scenario = await SetupPublicWorldAsync("black-harbor");

        var underOldSlug = await _anonymous.GetAsync("/api/public/worlds/black-harbor");
        Assert.That(underOldSlug.StatusCode, Is.EqualTo(HttpStatusCode.OK), "arrangement check");

        await SetPublicAsync(scenario, "blackwater-harbor", enabled: true);

        var oldSlug = await _anonymous.GetAsync("/api/public/worlds/black-harbor");
        var newSlug = await _anonymous.GetAsync("/api/public/worlds/blackwater-harbor");

        Assert.Multiple(() =>
        {
            Assert.That(oldSlug.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(newSlug.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task DeletingTheWorld_StopsItBeingServed()
    {
        var scenario = await SetupPublicWorldAsync("black-harbor");
        Assert.That((await _anonymous.GetAsync("/api/public/worlds/black-harbor")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "arrangement check");

        var deleted = await scenario.GmClient.DeleteAsync(
            $"/api/worlds/{scenario.World.Id}?confirmName={Uri.EscapeDataString(scenario.World.Name)}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            await deleted.Content.ReadAsStringAsync());

        var afterDelete = await _anonymous.GetAsync("/api/public/worlds/black-harbor");

        Assert.That(afterDelete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "content that no longer exists cannot be served from cache");
    }

    [Test]
    public async Task HidingAPublishedSource_TakesItDownForStrangersToo()
    {
        // The case the first cut of this got wrong, and the one that matters most. A GM spots a
        // player's real name in a published session note and sets it to GMOnly. Evicting only on
        // world settings left the note being served to anonymous visitors for the rest of the
        // cache window — and the GM could not see it happening, because their own requests carry a
        // bearer token and output caching declines to serve those.
        var scenario = await SetupPublicWorldAsync("black-harbor");

        var created = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/sources",
            new CreateSourceRequest("Session 1", "SessionNote", "PartyVisible", Body: "We sailed."));
        var sourceId = (await created.Content.ReadFromJsonAsync<SourceResponse>())!.Id;
        await MarkProcessedAsync(sourceId);

        var visible = await _anonymous.GetAsync(
            $"/api/public/worlds/black-harbor/sources/{sourceId}");
        Assert.That(visible.StatusCode, Is.EqualTo(HttpStatusCode.OK), "arrangement check");

        var hide = await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{sourceId}",
            new UpdateSourceRequest(Visibility: nameof(VisibilityScope.GMOnly)));
        Assert.That(hide.StatusCode, Is.EqualTo(HttpStatusCode.OK), await hide.Content.ReadAsStringAsync());

        var afterHiding = await _anonymous.GetAsync(
            $"/api/public/worlds/black-harbor/sources/{sourceId}");

        Assert.That(afterHiding.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "a takedown that only takes effect in a minute is not a takedown");
    }

    // ------------------------------------------------------------------ the cache key

    [Test]
    public async Task AQueryStringDoesNotMintANewEntry()
    {
        // None of these endpoints reads Request.Query, so ?_=1 and ?_=2 are the same response.
        // Varying on them — which is the framework default — means every distinct query string is
        // a guaranteed miss, so walking ?_=1..n against a real slug fills the store and burns the
        // shared anonymous rate-limit budget while never being served from cache once.
        await SetupPublicWorldAsync("black-harbor");

        var warm = await GetWorldAsync("black-harbor");

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            context.Worlds.Single(w => w.PublicSlug == "black-harbor").Name = "Renamed Behind The Cache";
            await context.SaveChangesAsync();
        }

        var withQuery = await _anonymous.GetFromJsonAsync<PublicWorldResponse>(
            "/api/public/worlds/black-harbor?_=cachebuster");

        Assert.That(withQuery!.Name, Is.EqualTo(warm!.Name),
            "a query string the endpoint never reads must not create a second cache entry");
    }

    // ------------------------------------------------------------------ scope

    [Test]
    public async Task AskIsNotCached()
    {
        // Ask is a paid model call behind a monthly cap. Caching it would either serve one
        // visitor's answer to another's question, or — worse — make the spend counter stop
        // matching what was actually asked.
        var controller = typeof(Nornis.Api.Controllers.PublicController);
        var ask = controller.GetMethod(nameof(Nornis.Api.Controllers.PublicController.Ask))!;

        var attributes = ask.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute), inherit: true);

        Assert.That(attributes, Is.Empty);
    }

    [Test]
    public void EveryPublicGetIsCached()
    {
        // Adding a public read and forgetting the attribute is silent — the endpoint simply costs
        // more than its neighbours. This fails the day one is added without it.
        var uncached = typeof(Nornis.Api.Controllers.PublicController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Length > 0)
            .Where(m => m.GetCustomAttributes(
                typeof(Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute), inherit: true).Length == 0)
            .Select(m => m.Name)
            .ToList();

        Assert.That(uncached, Is.Empty, "public GETs missing the output-cache policy");
    }

    [Test]
    public void NoAuthenticatedControllerOptsIntoTheCache()
    {
        // The rule the policy's own doc comment states, enforced. Authenticated responses vary by
        // user, world role and the view-as header; a cached one would hand a reader someone else's
        // view of a world. PublicController is the only place this belongs.
        var offenders = typeof(Nornis.Api.Controllers.PublicController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t != typeof(Nornis.Api.Controllers.PublicController))
            .SelectMany(t => t
                .GetMethods(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(m => (Type: t, Member: (System.Reflection.MemberInfo)m))
                // The type itself, not just its actions: a class-level [OutputCache] opts every
                // action in at once and a method-only scan would not see it.
                .Append((Type: t, Member: t)))
            .Where(x => x.Member.GetCustomAttributes(
                typeof(Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute), inherit: true).Length > 0)
            .Select(x => $"{x.Type.Name}.{x.Member.Name}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "output caching outside the anonymous public surface would serve one reader another's view");
    }

    // ------------------------------------------------------------------ it does cache

    [Test]
    public async Task RepeatedReadsAreServedWithoutRequeryingTheDatabase()
    {
        // The saving itself. Without an assertion here the whole change could be inert — every
        // test above passes just as well with no caching at all.
        await SetupPublicWorldAsync("black-harbor");

        // Warm it, then change the underlying row behind the API's back. A cached response still
        // shows the old name; an uncached one would pick the new one up.
        var before = await GetWorldAsync("black-harbor");

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var world = context.Worlds.Single(w => w.PublicSlug == "black-harbor");
            world.Name = "Renamed Behind The Cache";
            await context.SaveChangesAsync();
        }

        var after = await GetWorldAsync("black-harbor");

        Assert.That(after!.Name, Is.EqualTo(before!.Name),
            "the second read was served from cache, so it did not see the write");
    }
}
