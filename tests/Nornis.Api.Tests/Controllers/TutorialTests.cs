using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

/// <summary>
/// Onboarding flags and the demo-world tutorial checklist (feature 20 phase C): one-way
/// per-user flags, state-detected steps, client-reported steps, resumability, and the
/// held-back Session 6 paste text served from the template package.
/// </summary>
[TestFixture]
public class TutorialTests
{
    private const string SessionSixBody = "# Session 6 — The Undertide\nBleakspire Keep, by the old drove road.";

    private NornisWebApplicationFactory _factory = null!;
    private string _templatePath = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _templatePath = Path.Combine(Path.GetTempPath(), $"nornis-test-template-{Guid.NewGuid():N}.zip");
        WriteTemplateZip(_templatePath);

        var withTemplate = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("DemoWorlds:TemplatePath", _templatePath));

        var token = TestJwtIssuer.GenerateToken("auth0|tutorial-user", "tutorial@vespergale.com", "Tutorial Tess");
        _client = withTemplate.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        try
        {
            File.Delete(_templatePath);
        }
        catch (IOException)
        {
        }
    }

    private async Task<WorldResponse> CreateDemoWorldAsync(bool tutorial = true)
    {
        var response = await _client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(tutorial));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<WorldResponse>())!;
    }

    // ------------------------------------------------------------------ onboarding --

    [Test]
    public async Task Onboarding_FlagsAreOneWay()
    {
        var initial = await _client.GetFromJsonAsync<OnboardingStateResponse>("/api/onboarding");
        Assert.That(initial!.PromptSeen, Is.False);
        Assert.That(initial.TutorialDismissed, Is.False);

        await _client.PostAsync("/api/onboarding/prompt-seen", null);
        await _client.PostAsync("/api/onboarding/dismiss-tutorial", null);

        var after = await _client.GetFromJsonAsync<OnboardingStateResponse>("/api/onboarding");
        Assert.That(after!.PromptSeen, Is.True);
        Assert.That(after.TutorialDismissed, Is.True);
    }

    // ------------------------------------------------------------------- checklist --

    [Test]
    public async Task Tutorial_OnNonDemoWorld_Returns404()
    {
        var created = await _client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Real World", null, null));
        var world = await created.Content.ReadFromJsonAsync<WorldResponse>();

        var response = await _client.GetAsync($"/api/worlds/{world!.Id}/tutorial");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Tutorial_FreshDemoWorld_HasAllStepsIncomplete()
    {
        var world = await CreateDemoWorldAsync();

        var checklist = await _client.GetFromJsonAsync<TutorialChecklistResponse>($"/api/worlds/{world.Id}/tutorial");

        Assert.That(checklist!.Steps, Has.Count.EqualTo(11));
        Assert.That(checklist.Steps.All(s => s.CompletedAt is null), Is.True);
        Assert.That(checklist.Steps.Count(s => s.Chapter == 1), Is.EqualTo(5));
        Assert.That(checklist.Steps.Count(s => s.Chapter == 2), Is.EqualTo(6));
    }

    [Test]
    public async Task Tutorial_ClientReportedStep_CompletesAndSticks()
    {
        var world = await CreateDemoWorldAsync();

        var report = await _client.PostAsync($"/api/worlds/{world.Id}/tutorial/steps/meet-the-cast", null);
        Assert.That(report.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Resumable: a later fetch still shows it complete.
        var checklist = await _client.GetFromJsonAsync<TutorialChecklistResponse>($"/api/worlds/{world.Id}/tutorial");
        Assert.That(checklist!.Steps.Single(s => s.Key == "meet-the-cast").CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Tutorial_StateBackedStep_CannotBeClientReported()
    {
        var world = await CreateDemoWorldAsync();

        var report = await _client.PostAsync($"/api/worlds/{world.Id}/tutorial/steps/add-session-six", null);

        Assert.That(report.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Tutorial_SeeWhatTheySee_RequiresARevealFirst()
    {
        var world = await CreateDemoWorldAsync();

        var early = await _client.PostAsync($"/api/worlds/{world.Id}/tutorial/steps/see-what-they-see", null);
        Assert.That(early.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        SeedRevealBatch(world.Id);

        var after = await _client.PostAsync($"/api/worlds/{world.Id}/tutorial/steps/see-what-they-see", null);
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Tutorial_AddingASource_CompletesAddSessionSix()
    {
        var world = await CreateDemoWorldAsync();

        var created = await _client.PostAsJsonAsync($"/api/worlds/{world.Id}/sources",
            new CreateSourceRequest("Session 6 — The Undertide", "SessionNote", "PartyVisible",
                Body: "Bleakspire Keep, by the old drove road."));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var checklist = await _client.GetFromJsonAsync<TutorialChecklistResponse>($"/api/worlds/{world.Id}/tutorial");

        Assert.That(checklist!.Steps.Single(s => s.Key == "add-session-six").CompletedAt, Is.Not.Null);
        // The new source has not processed yet, so "watch it think" stays open.
        Assert.That(checklist.Steps.Single(s => s.Key == "watch-extraction").CompletedAt, Is.Null);
    }

    [Test]
    public async Task Tutorial_DecidedProposalAndReveal_AreDetected()
    {
        var world = await CreateDemoWorldAsync();

        SeedDecidedProposal(world.Id);
        SeedRevealBatch(world.Id);

        var checklist = await _client.GetFromJsonAsync<TutorialChecklistResponse>($"/api/worlds/{world.Id}/tutorial");

        Assert.That(checklist!.Steps.Single(s => s.Key == "vet-extraction").CompletedAt, Is.Not.Null);
        Assert.That(checklist.Steps.Single(s => s.Key == "reveal-secret").CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Tutorial_SessionSix_ComesFromTheTemplatePackage()
    {
        var world = await CreateDemoWorldAsync();

        var response = await _client.GetFromJsonAsync<TutorialSessionSixResponse>(
            $"/api/worlds/{world.Id}/tutorial/session-six");

        Assert.That(response!.Body, Is.EqualTo(SessionSixBody));
    }

    // ------------------------------------------------------------------ seeding --

    private void SeedRevealBatch(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var sourceId = db.Sources.First(s => s.WorldId == worldId).Id;

        db.ReviewBatches.Add(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Completed,
            Kind = "Reveal",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private void SeedDecidedProposal(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var sourceId = db.Sources.First(s => s.WorldId == worldId).Id;
        var userId = db.Users.First().Id;

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ReviewBatches.Add(batch);
        db.ReviewProposals.Add(new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.AddFact,
            TargetType = ReviewTargetType.ArtifactFact,
            ProposedValueJson = "{}",
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow,
            ReviewedAt = DateTimeOffset.UtcNow,
            ReviewedByUserId = userId,
        });
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ fixture zip --

    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid ArtifactId = Guid.NewGuid();

    /// <summary>Minimal template: one source, one artifact, and the tutorial paste text.</summary>
    private static void WriteTemplateZip(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "world.json", $$"""
            {"id":"{{Guid.NewGuid()}}","name":"The Vespergale Reach",
             "description":"Demo.","gameSystem":"D&D 5e"}
            """);

        WriteEntry(zip, "sources.json", $$"""
            {"sources":[{"id":"{{SourceId}}","campaignId":null,"type":"SessionNote",
               "title":"Session 1","body":"Wrecks.","uri":null,
               "occurredAt":"2026-01-02T00:00:00Z","visibility":"PartyVisible",
               "processingStatus":"Processed","extractionEnabled":true,"derivedText":null}],
             "extractions":[],"references":[]}
            """);

        WriteEntry(zip, "codex.json", $$"""
            {"artifacts":[{"id":"{{ArtifactId}}","type":"Location","name":"Harrowport",
               "summary":null,"visibility":"PartyVisible","confidence":0.9,"status":"Active"}],
             "facts":[],"relationships":[]}
            """);

        WriteEntry(zip, "tutorial/session-6.md", SessionSixBody);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
