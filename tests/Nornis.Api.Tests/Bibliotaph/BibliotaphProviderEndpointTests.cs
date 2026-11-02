using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Bibliotaph;

/// <summary>
/// What Nornis provides to Bibliotaph's Nornis storage provider (feature 31, Requirement 5), and
/// the sweep that keeps each Bibliotaph-facing token on its own side of the API.
/// </summary>
[TestFixture]
[Category("Authorization")]
public class BibliotaphProviderEndpointTests
{
    private const string ProviderPrefix = "api/bibliotaph/v1";

    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task Sweep_BibliotaphTokens_ReachOnlyTheProviderRoutes_AndWebTokensNeverDo()
    {
        const string sub = "auth0|bibliotaph-sweep";
        await _factory.CreateAuthenticatedClient(sub: sub).GetAsync("/api/worlds");
        var bibliotaph = ClientWith(TestJwtIssuer.GenerateBibliotaphToken(sub));
        var web = ClientWith(TestJwtIssuer.GenerateToken(sub, "sweep@example.com", [new Claim("azp", TestJwtIssuer.WebClientId)]));

        var routes = ProtectedRoutes();
        Assert.That(routes.Count(r => r.Path.StartsWith("/" + ProviderPrefix)), Is.EqualTo(5), "the five provider routes");
        Assert.That(routes, Has.Count.GreaterThan(50), "the sweep found the API's routes");

        var leaks = new List<string>();
        foreach (var (method, path) in routes)
        {
            var isProvider = path.StartsWith("/" + ProviderPrefix);
            var client = isProvider ? web : bibliotaph;
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                leaks.Add($"{method} {path} answered {(int)response.StatusCode} to a {(isProvider ? "Web" : "Bibliotaph")} token");
            }
        }

        Assert.That(leaks, Is.Empty);
    }

    [Test]
    public async Task UnknownSubject_Is401_AndCreatesNoUser()
    {
        const string sub = "auth0|bibliotaph-provider-stranger";

        var response = await ClientWith(TestJwtIssuer.GenerateBibliotaphToken(sub)).GetAsync($"/{ProviderPrefix}/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await DbAsync(db => db.Users.AnyAsync(u => u.Auth0SubjectId == sub)), Is.False);
    }

    [Test]
    public async Task WithoutTheScope_Is403()
    {
        const string sub = "auth0|bibliotaph-provider-noscope";
        await _factory.CreateAuthenticatedClient(sub: sub).GetAsync("/api/worlds");

        var response = await ClientWith(TestJwtIssuer.GenerateBibliotaphToken(sub, scope: "openid offline_access"))
            .GetAsync($"/{ProviderPrefix}/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Me_NamesTheUser()
    {
        var (client, user) = await ProviderClientAsync("auth0|bibliotaph-provider-me");

        var me = await client.GetFromJsonAsync<MeDto>($"/{ProviderPrefix}/me");

        Assert.That(me!.UserId, Is.EqualTo(user.Id));
        Assert.That(me.DisplayName, Is.EqualTo(user.Username));
    }

    [Test]
    public async Task Worlds_ListOnlyTheCallersOwn()
    {
        var (client, user) = await ProviderClientAsync("auth0|bibliotaph-provider-worlds");
        var (_, other) = await ProviderClientAsync("auth0|bibliotaph-provider-worlds-other");
        var own = NewWorld("Gribbits", user.Id);
        await SeedAsync(db => db.Worlds.AddRange(
            own, NewWorld("Tutorial", user.Id, demo: true), NewWorld("Someone else's", other.Id)));

        var page = await client.GetFromJsonAsync<WorldsDto>($"/{ProviderPrefix}/worlds");

        Assert.That(page!.Worlds.Select(w => w.WorldId), Is.EqualTo([own.Id]));
        Assert.That(page.Worlds[0].Name, Is.EqualTo("Gribbits"));
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task Files_PageThrough250_AndNeverListAHandoutMapOrPendingUpload()
    {
        var (client, user) = await ProviderClientAsync("auth0|bibliotaph-provider-files");
        var world = NewWorld("Gribbits", user.Id);
        var books = Enumerable.Range(0, 250)
            .Select(i => NewDocument(world.Id, user.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Indexed, $"book-{i}.pdf"))
            .ToList();
        await SeedAsync(db =>
        {
            db.Worlds.Add(world);
            db.LibraryDocuments.AddRange(books);
            db.LibraryDocuments.AddRange(
                NewDocument(world.Id, user.Id, LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored, "handout.pdf"),
                NewDocument(world.Id, user.Id, LibraryDocumentKind.Map, LibraryDocumentStatus.Stored, "map.png"),
                NewDocument(world.Id, user.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.PendingUpload, "pending.pdf"));
        });

        var seen = new List<FileDto>();
        var pages = 0;
        string? cursor = null;
        do
        {
            var url = $"/{ProviderPrefix}/worlds/{world.Id}/files" + (cursor is null ? "" : $"?cursor={cursor}");
            var page = await client.GetFromJsonAsync<FilesDto>(url);
            seen.AddRange(page!.Files);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.That(pages, Is.EqualTo(3));
        Assert.That(seen.Select(f => f.FileId), Is.EquivalentTo(books.Select(b => b.Id)));
        Assert.That(seen[0].Etag, Is.Not.Empty);
    }

    [Test]
    public async Task AnotherUsersWorld_AndAMadeUpCursor_AreRefused()
    {
        var (client, _) = await ProviderClientAsync("auth0|bibliotaph-provider-refusals");
        var (_, other) = await ProviderClientAsync("auth0|bibliotaph-provider-refusals-other");
        var theirs = NewWorld("Theirs", other.Id);
        var theirBook = NewDocument(theirs.Id, other.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Stored, "theirs.pdf");
        await SeedAsync(db =>
        {
            db.Worlds.Add(theirs);
            db.LibraryDocuments.Add(theirBook);
        });

        Assert.That((await client.GetAsync($"/{ProviderPrefix}/worlds/{theirs.Id}/files")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/{ProviderPrefix}/files/{theirBook.Id}/link")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/{ProviderPrefix}/worlds?cursor=not-a-cursor")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Link_IsAReadAddressForAboutAnHour_AndNeverForAMap()
    {
        var (client, user) = await ProviderClientAsync("auth0|bibliotaph-provider-link");
        var world = NewWorld("Gribbits", user.Id);
        var book = NewDocument(world.Id, user.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Stored, "book.pdf");
        var map = NewDocument(world.Id, user.Id, LibraryDocumentKind.Map, LibraryDocumentStatus.Stored, "map.png");
        await SeedAsync(db =>
        {
            db.Worlds.Add(world);
            db.LibraryDocuments.AddRange(book, map);
        });

        var link = await client.GetFromJsonAsync<LinkDto>($"/{ProviderPrefix}/files/{book.Id}/link");

        Assert.That(link!.Url, Does.Contain(book.BlobPath));
        Assert.That(link.ExpiresAt, Is.EqualTo(DateTimeOffset.UtcNow.AddHours(1)).Within(TimeSpan.FromMinutes(2)));
        Assert.That((await client.GetAsync($"/{ProviderPrefix}/files/{map.Id}/link")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Resync_QueuesCreatedForEachWorld_WhenConnected()
    {
        var (client, user) = await ProviderClientAsync("auth0|bibliotaph-provider-resync");
        await SeedAsync(db => db.Worlds.AddRange(NewWorld("One", user.Id), NewWorld("Two", user.Id)));

        var unconnected = await client.PostAsync($"/{ProviderPrefix}/resync", null);
        var queuedUnconnected = await DbAsync(db => db.BibliotaphWorldEvents.CountAsync(e => e.UserId == user.Id));

        await SeedAsync(db => db.BibliotaphConnections.Add(new BibliotaphConnection
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ProtectedRefreshToken = "protected",
            Status = BibliotaphConnectionStatus.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
            StatusChangedAt = DateTimeOffset.UtcNow,
        }));
        var connected = await client.PostAsync($"/{ProviderPrefix}/resync", null);

        Assert.That(unconnected.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(queuedUnconnected, Is.Zero);
        Assert.That(connected.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var kinds = await DbAsync(db => db.BibliotaphWorldEvents.Where(e => e.UserId == user.Id).Select(e => e.Kind).ToListAsync());
        Assert.That(kinds, Is.EqualTo([BibliotaphWorldEventKind.Created, BibliotaphWorldEventKind.Created]));
    }

    private sealed record MeDto(Guid UserId, string DisplayName);

    private sealed record WorldDto(Guid WorldId, string Name, DateTimeOffset UpdatedAt);

    private sealed record WorldsDto(List<WorldDto> Worlds, string? NextCursor);

    private sealed record FileDto(Guid FileId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UpdatedAt, string Etag);

    private sealed record FilesDto(List<FileDto> Files, string? NextCursor);

    private sealed record LinkDto(string Url, DateTimeOffset ExpiresAt);

    /// <summary>Every routed, non-anonymous endpoint, with its parameters filled in.</summary>
    private List<(string Method, string Path)> ProtectedRoutes()
    {
        var sources = _factory.Services.GetServices<EndpointDataSource>();
        return sources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => (Method: m, Path: "/" + Fill(e.RoutePattern.RawText ?? ""))))
            .Where(r => !r.Path.Contains("{"))
            .Distinct()
            .ToList();
    }

    private static string Fill(string pattern) =>
        Regex.Replace(pattern.TrimStart('/'), @"\{\*?(?<name>[^}:=?]+)(?<constraint>:[^}=?]+)?[^}]*\}", m =>
            m.Groups["constraint"].Value.Contains("int") ? "1" : Guid.NewGuid().ToString());

    private async Task<(HttpClient Client, User User)> ProviderClientAsync(string sub)
    {
        await _factory.CreateAuthenticatedClient(sub: sub, email: $"{Guid.NewGuid():N}@example.com").GetAsync("/api/worlds");
        var user = await DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Auth0SubjectId == sub));
        return (ClientWith(TestJwtIssuer.GenerateBibliotaphToken(sub)), user);
    }

    private async Task SeedAsync(Action<NornisDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<T> DbAsync<T>(Func<NornisDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<NornisDbContext>());
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static World NewWorld(string name, Guid creator, bool demo = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = creator,
        IsDemo = demo,
        RowVersion = [],
    };

    private static LibraryDocument NewDocument(
        Guid worldId, Guid userId, LibraryDocumentKind kind, LibraryDocumentStatus status, string fileName) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Title = fileName,
            FileName = fileName,
            ContentType = "application/pdf",
            SizeBytes = 1234,
            BlobPath = $"worlds/{worldId}/library/{Guid.NewGuid()}/{fileName}",
            Kind = kind,
            Visibility = VisibilityScope.GMOnly,
            Status = status,
            UploadedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
