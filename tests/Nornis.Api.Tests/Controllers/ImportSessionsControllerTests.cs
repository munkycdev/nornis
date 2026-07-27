using System.Net;
using System.Net.Http.Json;
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
/// The campaign backlog import over HTTP: GM-only, one non-terminal session per world, and
/// an advance that refuses to move on while the note on screen still has open proposals.
/// </summary>
[TestFixture]
public class ImportSessionsControllerTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _gm = null!;
    private Guid _worldId;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _gm = _factory.CreateAuthenticatedClient(
            sub: "auth0|import-gm", email: "gm@blackharbor.com", nickname: "GM Voss");

        var created = await _gm.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Black Harbor Investigation", "A dark mystery", "D&D 5e"));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        _worldId = (await created.Content.ReadFromJsonAsync<WorldResponse>())!.Id;
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    private string Base => $"/api/worlds/{_worldId}/import-sessions";

    private async Task<ImportSessionResponse> CreateSessionAsync()
    {
        var response = await _gm.PostAsync(Base, null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<ImportSessionResponse>())!;
    }

    private async Task<ImportSessionResponse> AddNoteAsync(Guid sessionId, string title)
    {
        var response = await _gm.PostAsJsonAsync($"{Base}/{sessionId}/items",
            new AddImportNoteRequest(title, $"Body of {title}"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportSessionResponse>())!;
    }

    /// <summary>Stands in for the worker: the queued note comes back Processed, optionally
    /// with an open proposal hanging off a batch.</summary>
    private async Task CompleteExtractionAsync(Guid sourceId, bool withOpenProposal)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var source = context.Sources.Single(s => s.Id == sourceId);
        source.ProcessingStatus = SourceProcessingStatus.Processed;

        if (withOpenProposal)
        {
            var batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = _worldId,
                SourceId = sourceId,
                Status = ReviewBatchStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.ReviewBatches.Add(batch);
            context.ReviewProposals.Add(new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                ProposedValueJson = """{"name":"Captain Voss","type":"Character"}""",
                Status = ReviewProposalStatus.Edited,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }

    [Test]
    public async Task Current_WithNoImport_Returns404()
    {
        var response = await _gm.GetAsync($"{Base}/current");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Create_Twice_Returns409()
    {
        await CreateSessionAsync();

        var second = await _gm.PostAsync(Base, null);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Player_CannotRunAnImport()
    {
        var player = _factory.CreateAuthenticatedClient(
            sub: "auth0|import-player", email: "player@blackharbor.com", nickname: "Tavrin");

        // Users are provisioned on their first authenticated request.
        await player.GetAsync("/api/worlds");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var playerUser = context.Users.Single(u => u.Auth0SubjectId == "auth0|import-player");
        context.WorldMembers.Add(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = playerUser.Id,
            Role = WorldRole.Player,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var response = await player.PostAsync(Base, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Walk_HoldsNotesUntilTheirTurnAndGatesOnReview()
    {
        var session = await CreateSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var withBoth = await AddNoteAsync(session.Id, "Session 2");

        // Nothing is queued while the backlog is still being assembled.
        Assert.That(_factory.ExtractionQueueClient.SentMessages, Is.Empty);
        Assert.That(withBoth.Items.Select(i => i.State), Is.All.EqualTo("Waiting"));

        var started = await _gm.PostAsync($"{Base}/{session.Id}/start", null);
        var afterStart = (await started.Content.ReadFromJsonAsync<ImportSessionResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(afterStart.Status, Is.EqualTo("InProgress"));
            Assert.That(afterStart.Items[0].State, Is.EqualTo("Extracting"));
            Assert.That(afterStart.Items[1].State, Is.EqualTo("Waiting"));
            Assert.That(_factory.ExtractionQueueClient.SentMessages, Has.Count.EqualTo(1));
        });

        // Extracted, but an Edited proposal is still open — the walk holds.
        await CompleteExtractionAsync(afterStart.Items[0].SourceId, withOpenProposal: true);

        var blocked = await _gm.PostAsJsonAsync(
            $"{Base}/{session.Id}/advance", new AdvanceImportSessionRequest(false));
        Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var reviewing = await _gm.GetFromJsonAsync<ImportSessionResponse>($"{Base}/current");
        Assert.Multiple(() =>
        {
            Assert.That(reviewing!.Items[0].State, Is.EqualTo("Reviewing"));
            Assert.That(reviewing.Items[0].OpenProposalCount, Is.EqualTo(1));
            Assert.That(reviewing.CurrentIndex, Is.EqualTo(1));
        });

        // Skipping is the sanctioned escape hatch, and it queues the next note.
        var skipped = await _gm.PostAsJsonAsync(
            $"{Base}/{session.Id}/advance", new AdvanceImportSessionRequest(true));
        var afterSkip = (await skipped.Content.ReadFromJsonAsync<ImportSessionResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(afterSkip.Items[0].State, Is.EqualTo("Skipped"));
            Assert.That(afterSkip.Items[1].State, Is.EqualTo("Extracting"));
            Assert.That(_factory.ExtractionQueueClient.SentMessages, Has.Count.EqualTo(2));
        });

        // Last note lands clean; advancing past it finishes the import.
        await CompleteExtractionAsync(afterSkip.Items[1].SourceId, withOpenProposal: false);
        var finished = await _gm.PostAsJsonAsync(
            $"{Base}/{session.Id}/advance", new AdvanceImportSessionRequest(false));
        var completed = (await finished.Content.ReadFromJsonAsync<ImportSessionResponse>())!;

        Assert.That(completed.Status, Is.EqualTo("Completed"));
        Assert.That((await _gm.GetAsync($"{Base}/current")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Reorder_And_Delete_ApplyToNotYetStartedNotes()
    {
        var session = await CreateSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var listed = await AddNoteAsync(session.Id, "Session 2");

        var reversed = listed.Items.Select(i => i.Id).Reverse().ToList();
        var reordered = await _gm.PutAsJsonAsync(
            $"{Base}/{session.Id}/items/order", new ReorderImportItemsRequest(reversed));
        var afterOrder = (await reordered.Content.ReadFromJsonAsync<ImportSessionResponse>())!;
        Assert.That(afterOrder.Items.Select(i => i.Title), Is.EqualTo(new[] { "Session 2", "Session 1" }));

        var removedSourceId = afterOrder.Items[0].SourceId;
        var deleted = await _gm.DeleteAsync($"{Base}/{session.Id}/items/{afterOrder.Items[0].Id}");
        var afterDelete = (await deleted.Content.ReadFromJsonAsync<ImportSessionResponse>())!;
        Assert.That(afterDelete.Items.Select(i => i.Title), Is.EqualTo(new[] { "Session 1" }));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(context.Sources.Any(s => s.Id == removedSourceId), Is.False,
            "the note was created by the import and goes with the item");
    }

    [Test]
    public async Task Abandon_LeavesEveryNoteInPlace()
    {
        var session = await CreateSessionAsync();
        var listed = await AddNoteAsync(session.Id, "Session 1");

        var abandoned = await _gm.PostAsync($"{Base}/{session.Id}/abandon", null);
        var after = (await abandoned.Content.ReadFromJsonAsync<ImportSessionResponse>())!;

        Assert.That(after.Status, Is.EqualTo("Abandoned"));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var source = context.Sources.Single(s => s.Id == listed.Items[0].SourceId);
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Draft));

        // The world is free to start over.
        Assert.That((await _gm.PostAsync(Base, null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
