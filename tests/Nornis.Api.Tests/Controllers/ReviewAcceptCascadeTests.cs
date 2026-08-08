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
/// Accept order inside a batch must not matter to the reviewer: accepting a fact or
/// relationship whose target artifact only exists as a sibling Create proposal accepts
/// the prerequisite first and retries. Field-reported 2026-07-26 ("approving the 8
/// extracted facts failed") — before this, such accepts errored with
/// "accept its Create proposal first".
///
/// The same report came back for names with no sibling Create at all, so the recovery now
/// continues past that rung: a name resembling something the reviewer has (or is about to
/// have) becomes a question, and a name resembling nothing is created. End to end here,
/// because the query flag that carries the reviewer's answer is part of the contract.
/// </summary>
[TestFixture]
public class ReviewAcceptCascadeTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _client = _factory.CreateAuthenticatedClient(
            sub: "auth0|cascade-gm", email: "cascade@vespergale.com", nickname: "Cascade GM");
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<(Guid WorldId, Guid CreateId, Guid FactId)> SeedBatchAsync(string factArtifactName)
    {
        var created = await _client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Cascade World", null, null));
        var world = (await created.Content.ReadFromJsonAsync<WorldResponse>())!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = SourceType.SessionNote,
            Title = "Session",
            Body = "Notes.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = db.Users.First().Id,
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var create = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"type":"Character","name":"Cascade Character","summary":"Seeded.","visibility":"PartyVisible","confidence":0.9}""",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var fact = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.AddFact,
            TargetType = ReviewTargetType.ArtifactFact,
            ProposedValueJson = $$"""{"artifactName":"{{factArtifactName}}","predicate":"is","value":"seeded","truthState":"Confirmed","visibility":"PartyVisible","confidence":0.9}""",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Sources.Add(source);
        db.ReviewBatches.Add(batch);
        db.ReviewProposals.AddRange(create, fact);
        db.SaveChanges();

        return (world.Id, create.Id, fact.Id);
    }

    [Test]
    public async Task AcceptingFactFirst_AcceptsItsCreatePrerequisite_ThenTheFact()
    {
        var (worldId, createId, factId) = await SeedBatchAsync("Cascade Character");

        var response = await _client.PostAsync($"/api/worlds/{worldId}/reviews/proposals/{factId}/accept", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.ReviewProposals.Find(createId)!.Status, Is.EqualTo(ReviewProposalStatus.Accepted),
            "the prerequisite Create must have been accepted by the cascade");
        Assert.That(db.ReviewProposals.Find(factId)!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(db.Artifacts.Any(a => a.WorldId == worldId && a.Name == "Cascade Character"), Is.True);
        Assert.That(db.ArtifactFacts.Any(f => f.Value == "seeded"), Is.True);
    }

    [Test]
    public async Task AcceptingFact_WithNoMatchingSiblingCreate_CreatesTheNameItReferences()
    {
        var (worldId, createId, factId) = await SeedBatchAsync("Somebody Else Entirely");

        var response = await _client.PostAsync($"/api/worlds/{worldId}/reviews/proposals/{factId}/accept", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = (await response.Content.ReadFromJsonAsync<AcceptProposalResponse>())!;
        Assert.That(body.CreatedMissingArtifactNames, Is.EqualTo(["Somebody Else Entirely"]));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.ReviewProposals.Find(factId)!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(db.Artifacts.Any(a => a.WorldId == worldId && a.Name == "Somebody Else Entirely"), Is.True);
        Assert.That(db.ReviewProposals.Find(createId)!.Status, Is.EqualTo(ReviewProposalStatus.Pending),
            "an unrelated Create must not be swept up by the cascade");
    }

    [Test]
    public async Task AcceptingFact_WhoseNameResemblesAnUndecidedSiblingCreate_AsksRatherThanTwinning()
    {
        // "Cascade" against the batch's own undecided "Cascade Character". Nothing resolves it
        // and canon is empty, so the create rung would fire — and produce the second artifact
        // moments before the first one exists. It asks instead.
        var (worldId, createId, factId) = await SeedBatchAsync("Cascade");

        var response = await _client.PostAsync($"/api/worlds/{worldId}/reviews/proposals/{factId}/accept", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var error = await response.Content.ReadAsStringAsync();
        Assert.That(error, Does.Contain("artifact_name_near_match"));
        Assert.That(error, Does.Contain("Cascade Character"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.Artifacts.Any(a => a.WorldId == worldId), Is.False);
        Assert.That(db.ReviewProposals.Find(createId)!.Status, Is.EqualTo(ReviewProposalStatus.Pending));
        Assert.That(db.ReviewProposals.Find(factId)!.Status, Is.EqualTo(ReviewProposalStatus.Pending),
            "a refused accept leaves the proposal reviewable");
    }

    [Test]
    public async Task CreateMissing_CarriesTheReviewersAnswerBackThrough()
    {
        var (worldId, _, factId) = await SeedBatchAsync("Cascade");

        var response = await _client.PostAsync(
            $"/api/worlds/{worldId}/reviews/proposals/{factId}/accept?createMissing=true", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.Artifacts.Any(a => a.WorldId == worldId && a.Name == "Cascade"), Is.True);
    }
}
