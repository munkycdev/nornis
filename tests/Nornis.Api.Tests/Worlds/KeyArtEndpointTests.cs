using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// Key art and the hero (feature 42) over HTTP. The card and every write are the GM's; the image and
/// the hero are any member's; the public routes serve only a public world (correctness property 2).
/// </summary>
[TestFixture]
public class KeyArtEndpointTests
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

    private static string Base(Guid worldId) => $"/api/worlds/{worldId}/key-art";

    /// <summary>A PNG header with the given size: the API reads only that.</summary>
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[40];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static IEnumerable<(string Name, Func<HttpClient, Guid, Task<HttpResponseMessage>> Send)> GmRoutes()
    {
        yield return ("card", (c, w) => c.GetAsync(Base(w)));
        yield return ("save brief", (c, w) => c.PutAsJsonAsync($"{Base(w)}/brief", new KeyArtBriefRequest("Mine")));
        yield return ("generate", (c, w) => c.PostAsJsonAsync($"{Base(w)}/generate", new KeyArtBriefRequest("A coast at dusk.")));
        yield return ("choose", (c, w) => c.PostAsJsonAsync($"{Base(w)}/choose", new ChooseKeyArtRequest(Guid.NewGuid())));
        yield return ("upload", (c, w) => c.PutAsync($"{Base(w)}/image", new ByteArrayContent(Png(2000, 1000))));
        yield return ("anchor", (c, w) => c.PutAsJsonAsync($"{Base(w)}/anchor", new SetKeyArtAnchorRequest("Top")));
        yield return ("remove", (c, w) => c.DeleteAsync(Base(w)));
        yield return ("option", (c, w) => c.GetAsync($"{Base(w)}/options/{Guid.NewGuid()}"));
    }

    [Test]
    public async Task Gm_UploadsKeyArt_AnchorsIt_EveryMemberSeesIt_AndRemovesIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;

        var empty = (await scenario.GmClient.GetFromJsonAsync<KeyArtStateResponse>(Base(worldId)))!;
        var upload = await scenario.GmClient.PutAsync($"{Base(worldId)}/image", new ByteArrayContent(Png(1200, 600)));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), await upload.Content.ReadAsStringAsync());
        var uploaded = (await upload.Content.ReadFromJsonAsync<KeyArtStateResponse>())!;
        var anchored = (await (await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/anchor", new SetKeyArtAnchorRequest("Bottom")))
            .Content.ReadFromJsonAsync<KeyArtStateResponse>())!;
        var badAnchor = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/anchor", new SetKeyArtAnchorRequest("Sideways"));
        var playerImage = await scenario.PlayerClient.GetAsync($"{Base(worldId)}/image");
        var observerHero = (await scenario.ObserverClient.GetFromJsonAsync<WorldHeroResponse>($"/api/worlds/{worldId}/hero"))!;
        var removed = (await (await scenario.GmClient.DeleteAsync(Base(worldId))).Content.ReadFromJsonAsync<KeyArtStateResponse>())!;
        var gone = await scenario.PlayerClient.GetAsync($"{Base(worldId)}/image");

        Assert.Multiple(async () =>
        {
            Assert.That(empty.KeyArt, Is.Null);
            Assert.That(empty.CanGenerate, Is.False, "no OpenAI key in the test world");
            Assert.That(empty.GenerateBlocker, Does.Contain("OpenAI key"));
            Assert.That(empty.ImagesCap, Is.EqualTo(8));
            Assert.That(uploaded.KeyArt!.Source, Is.EqualTo("Uploaded"));
            Assert.That(uploaded.Warning, Does.Contain("1200 pixels wide"));
            Assert.That(anchored.KeyArt!.Anchor, Is.EqualTo("Bottom"));
            Assert.That(anchored.KeyArt.FocusY, Is.EqualTo(100));
            Assert.That(badAnchor.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(playerImage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(playerImage.Content.Headers.ContentType!.MediaType, Is.EqualTo("image/png"));
            Assert.That(await playerImage.Content.ReadAsByteArrayAsync(), Is.EqualTo(Png(1200, 600)));
            Assert.That(observerHero.Picture.Kind, Is.EqualTo("KeyArt"));
            Assert.That(observerHero.Picture.FocusY, Is.EqualTo(100));
            Assert.That(observerHero.Name, Is.EqualTo(scenario.World.Name));
            Assert.That(removed.KeyArt, Is.Null);
            Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Generating_WithNoOpenAiKey_IsRefusedWithTheKeyCallToAction_AndCostsNothing()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/generate", new KeyArtBriefRequest("A coast at dusk."));
        var after = (await scenario.GmClient.GetFromJsonAsync<KeyArtStateResponse>(Base(worldId)))!;

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await response.Content.ReadAsStringAsync());
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("ai_key_required"));
        Assert.That(after.ImagesThisMonth, Is.Zero, "a call that never ran gives its images back");
        Assert.That(after.Options, Is.Empty);
    }

    [Test]
    [Category("Authorization")]
    public async Task EveryGmRoute_RefusesPlayersObserversAndAGmViewingAsAPlayer()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var viewingAsPlayer = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-kelda-source", email: "kelda@blackharbor.com", nickname: "Kelda");
        viewingAsPlayer.DefaultRequestHeaders.Add(HttpContextExtensions.ViewAsHeaderName, "Player");

        var callers = new[] { ("player", scenario.PlayerClient), ("observer", scenario.ObserverClient), ("gm as player", viewingAsPlayer) };
        var failures = new List<string>();
        foreach (var (caller, client) in callers)
        {
            foreach (var (route, send) in GmRoutes())
            {
                var response = await send(client, worldId);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{caller} {route}: {(int)response.StatusCode}");
                }
            }
        }

        Assert.That(failures, Is.Empty);
        var after = (await scenario.GmClient.GetFromJsonAsync<KeyArtStateResponse>(Base(worldId)))!;
        Assert.That((after.KeyArt, after.Brief), Is.EqualTo(((KeyArtResponse?)null, (string?)null)), "nothing they sent landed");
    }

    [Test]
    [Category("Authorization")]
    public async Task AStranger_GetsNoKeyArtNorHero()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        await scenario.GmClient.PutAsync($"{Base(worldId)}/image", new ByteArrayContent(Png(2000, 1000)));
        var stranger = _factory.CreateAuthenticatedClient(sub: "auth0|stranger-keyart", email: "stranger@example.com", nickname: "Stranger");

        var image = await stranger.GetAsync($"{Base(worldId)}/image");
        var hero = await stranger.GetAsync($"/api/worlds/{worldId}/hero");
        var anonymous = await _anonymous.GetAsync($"{Base(worldId)}/image");

        Assert.Multiple(() =>
        {
            Assert.That(image.StatusCode, Is.AnyOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound));
            Assert.That(hero.StatusCode, Is.AnyOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound));
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task ThePublicHeroAndArt_AreServedOnlyForAPublicWorld()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        await scenario.GmClient.PutAsync($"{Base(worldId)}/image", new ByteArrayContent(Png(2000, 1000)));
        var slugged = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{worldId}",
            new UpdateWorldRequest(PublicSlug: "black-harbor-art", PublicAccessEnabled: false));
        Assert.That(slugged.StatusCode, Is.EqualTo(HttpStatusCode.OK), await slugged.Content.ReadAsStringAsync());

        var privateHero = await _anonymous.GetAsync("/api/public/worlds/black-harbor-art/hero");
        var privateArt = await _anonymous.GetAsync("/api/public/worlds/black-harbor-art/key-art");

        await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{worldId}", new UpdateWorldRequest(PublicAccessEnabled: true));
        var publicHero = await _anonymous.GetAsync("/api/public/worlds/black-harbor-art/hero");
        var publicArt = await _anonymous.GetAsync("/api/public/worlds/black-harbor-art/key-art");

        Assert.Multiple(async () =>
        {
            Assert.That(privateHero.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(privateArt.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(publicHero.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await publicHero.Content.ReadFromJsonAsync<WorldHeroResponse>())!.Picture.Kind, Is.EqualTo("KeyArt"));
            Assert.That(publicArt.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(publicArt.Headers.CacheControl!.Public, Is.True);
        });
    }
}
