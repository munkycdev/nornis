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

namespace Nornis.Api.Tests.Sources;

/// <summary>
/// Endpoint wiring for edit-and-reprocess: GET reprocess-preview and POST reprocess,
/// plus the relaxed PUT guard (metadata edits allowed on Processed sources, body
/// changes rejected with a pointer to the reprocess flow).
/// </summary>
[TestFixture]
[Category("Feature: source-reprocess")]
public class SourceReprocessEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    /// <summary>
    /// Seeds a processed source with one accepted CreateArtifact proposal whose artifact
    /// nothing else has built on — the minimal cascade: one orphan artifact to delete.
    /// </summary>
    private async Task<(Source Source, Artifact Artifact)> SeedProcessedSourceWithArtifactAsync()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            processingStatus: SourceProcessingStatus.Processed,
            body: "We questioned Captain Voss in Black Harbor.");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Artifacts.Add(artifact);

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        db.ReviewBatches.Add(batch);

        db.ReviewProposals.Add(new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = artifact.Id,
            ProposedValueJson = "{}",
            Rationale = "test",
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        });

        db.SourceReferences.Add(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = SourceReferenceTargetType.Artifact,
            TargetId = artifact.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return (source, artifact);
    }

    [Test]
    public async Task Preview_ReturnsCascadeCounts()
    {
        var (source, artifact) = await SeedProcessedSourceWithArtifactAsync();

        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/reprocess-preview");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var preview = await response.Content.ReadFromJsonAsync<ReprocessPreviewResponse>();
        Assert.That(preview!.ArtifactNamesToDelete, Is.EquivalentTo(new[] { artifact.Name }));
        Assert.That(preview.ArtifactNamesToKeep, Is.Empty);
        Assert.That(preview.FactsToDelete, Is.Zero);
    }

    [Test]
    public async Task Reprocess_DeletesDerivedKnowledgeAndRequeues()
    {
        var (source, artifact) = await SeedProcessedSourceWithArtifactAsync();

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/reprocess",
            new ReprocessSourceRequest(Body: "It was actually Lieutenant Voss."));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(updated!.ProcessingStatus, Is.EqualTo("Queued"));
        Assert.That(updated.Body, Is.EqualTo("It was actually Lieutenant Voss."));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.Artifacts.Any(a => a.Id == artifact.Id), Is.False, "orphan artifact is deleted");
        Assert.That(db.ReviewBatches.Any(b => b.SourceId == source.Id), Is.False, "batches are deleted");
        Assert.That(db.SourceReferences.Any(r => r.SourceId == source.Id), Is.False, "references are deleted");

        Assert.That(_factory.ExtractionQueueClient.SentMessages,
            Has.Some.Matches<(Guid SourceId, Guid WorldId)>(m => m.SourceId == source.Id),
            "extraction is requeued");
    }

    [Test]
    public async Task Reprocess_Observer_Returns403()
    {
        var (source, _) = await SeedProcessedSourceWithArtifactAsync();

        var response = await _scenario.ObserverClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/reprocess",
            new ReprocessSourceRequest(Body: "changed"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Reprocess_DraftSource_Returns409()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            processingStatus: SourceProcessingStatus.Draft);

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/reprocess",
            new ReprocessSourceRequest(Body: "changed"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Update_ProcessedSource_MetadataEditIsAllowed()
    {
        var (source, _) = await SeedProcessedSourceWithArtifactAsync();

        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}",
            new UpdateSourceRequest(Title: "Session 4 (renamed)"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(updated!.Title, Is.EqualTo("Session 4 (renamed)"));
        Assert.That(updated.ProcessingStatus, Is.EqualTo("Processed"), "metadata edits do not requeue");
    }

    [Test]
    public async Task Update_ProcessedSource_BodyChange_Returns409WithReprocessPointer()
    {
        var (source, _) = await SeedProcessedSourceWithArtifactAsync();

        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}",
            new UpdateSourceRequest(Body: "a different body"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("body_requires_reprocess"));
    }

    [Test]
    public async Task Update_ProcessedSource_UnchangedBodyResent_IsAllowed()
    {
        // Clients resend the whole form; an unchanged body must not trip the guard.
        var (source, _) = await SeedProcessedSourceWithArtifactAsync();

        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}",
            new UpdateSourceRequest(Title: "Renamed", Body: source.Body));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
