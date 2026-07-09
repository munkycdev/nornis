using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Reviews;

/// <summary>
/// End-to-end integration tests for the full review proposal workflow through HTTP.
/// These tests exercise the complete request pipeline: authentication, world membership,
/// review service logic, knowledge graph mutations, and response serialization.
/// 
/// Validates: Requirements 1.1, 2.1, 3.1, 4.1, 5.1, 7.4, 10.1, 10.3, 11.1
/// </summary>
[TestFixture]
public class ReviewWorkflowIntegrationTests
{
    private NornisWebApplicationFactory _factory = null!;
    private ReviewTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SetupReviewScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _scenario.ObserverClient.Dispose();
        _factory.Dispose();
    }

    private string ReviewsUrl => $"/api/worlds/{_scenario.World.Id}/reviews";

    #region List Queue → Accept → Verify Artifact Created

    [Test]
    public async Task ListQueue_AcceptCreateArtifact_CreatesArtifactWithCorrectFields()
    {
        // Arrange — seed a CreateArtifact proposal
        var proposalId = await SeedCreateArtifactProposalAsync(
            name: "Captain Voss",
            type: "Character",
            summary: "A harbor captain suspected of smuggling",
            visibility: "PartyVisible",
            confidence: 0.85m);

        // Act — list the queue and verify the proposal is there
        var listResponse = await _scenario.GmClient.GetAsync($"{ReviewsUrl}/proposals");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var queue = await listResponse.Content.ReadFromJsonAsync<ReviewQueueResponse>();
        Assert.That(queue, Is.Not.Null);
        Assert.That(queue!.Proposals.Any(p => p.Id == proposalId), Is.True);

        var proposal = queue.Proposals.First(p => p.Id == proposalId);
        Assert.That(proposal.ChangeType, Is.EqualTo("CreateArtifact"));
        Assert.That(proposal.Status, Is.EqualTo("Pending"));

        // Act — accept the proposal
        var acceptResponse = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/accept", null);
        Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AcceptProposalResponse>();
        Assert.That(acceptResult, Is.Not.Null);
        Assert.That(acceptResult!.ProposalId, Is.EqualTo(proposalId));
        Assert.That(acceptResult.Status, Is.EqualTo("Accepted"));
        Assert.That(acceptResult.ReviewedByUserId, Is.EqualTo(_scenario.GmUserId));
        Assert.That(acceptResult.ReviewedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(acceptResult.CreatedEntityId, Is.Not.Null);

        // Assert — verify the artifact was created in the database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var artifact = db.Artifacts.FirstOrDefault(a => a.Id == acceptResult.CreatedEntityId);
        Assert.That(artifact, Is.Not.Null);
        Assert.That(artifact!.Name, Is.EqualTo("Captain Voss"));
        Assert.That(artifact.Type, Is.EqualTo(ArtifactType.Character));
        Assert.That(artifact.Summary, Is.EqualTo("A harbor captain suspected of smuggling"));
        Assert.That(artifact.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(artifact.Confidence, Is.EqualTo(0.85m));
        Assert.That(artifact.Status, Is.EqualTo(ArtifactStatus.Active));
        Assert.That(artifact.WorldId, Is.EqualTo(_scenario.World.Id));

        // Verify SourceReference was created
        var sourceRef = db.SourceReferences.FirstOrDefault(
            sr => sr.TargetId == artifact.Id && sr.TargetType == SourceReferenceTargetType.Artifact);
        Assert.That(sourceRef, Is.Not.Null);
        Assert.That(sourceRef!.SourceId, Is.EqualTo(_scenario.GmSource.Id));
    }

    #endregion

    #region List Queue → Reject → No Knowledge Graph Changes

    [Test]
    public async Task ListQueue_RejectProposal_NoKnowledgeGraphChanges()
    {
        // Arrange — seed a CreateArtifact proposal
        var proposalId = await SeedCreateArtifactProposalAsync(
            name: "Black Harbor",
            type: "Location",
            summary: "A dark port city");

        // Capture initial database state
        int initialArtifactCount, initialFactCount, initialRelationshipCount, initialSourceRefCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            initialArtifactCount = db.Artifacts.Count();
            initialFactCount = db.ArtifactFacts.Count();
            initialRelationshipCount = db.ArtifactRelationships.Count();
            initialSourceRefCount = db.SourceReferences.Count();
        }

        // Act — list queue to confirm the proposal is present
        var listResponse = await _scenario.GmClient.GetAsync($"{ReviewsUrl}/proposals");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var queue = await listResponse.Content.ReadFromJsonAsync<ReviewQueueResponse>();
        Assert.That(queue!.Proposals.Any(p => p.Id == proposalId), Is.True);

        // Act — reject the proposal
        var rejectResponse = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/reject", null);
        Assert.That(rejectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var rejectResult = await rejectResponse.Content.ReadFromJsonAsync<RejectProposalResponse>();
        Assert.That(rejectResult, Is.Not.Null);
        Assert.That(rejectResult!.ProposalId, Is.EqualTo(proposalId));
        Assert.That(rejectResult.Status, Is.EqualTo("Rejected"));
        Assert.That(rejectResult.ReviewedByUserId, Is.EqualTo(_scenario.GmUserId));

        // Assert — no knowledge graph changes
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(verifyDb.Artifacts.Count(), Is.EqualTo(initialArtifactCount));
        Assert.That(verifyDb.ArtifactFacts.Count(), Is.EqualTo(initialFactCount));
        Assert.That(verifyDb.ArtifactRelationships.Count(), Is.EqualTo(initialRelationshipCount));
        Assert.That(verifyDb.SourceReferences.Count(), Is.EqualTo(initialSourceRefCount));
    }

    #endregion

    #region List Queue → Edit → Accept Edited → Verify Edited JSON Used

    [Test]
    public async Task ListQueue_EditThenAccept_UsesEditedJson()
    {
        // Arrange — seed a CreateArtifact proposal with original values
        var proposalId = await SeedCreateArtifactProposalAsync(
            name: "Silver Key",
            type: "Item",
            summary: "A mysterious key");

        // Act — verify proposal is in queue
        var listResponse = await _scenario.GmClient.GetAsync($"{ReviewsUrl}/proposals");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var queue = await listResponse.Content.ReadFromJsonAsync<ReviewQueueResponse>();
        Assert.That(queue!.Proposals.Any(p => p.Id == proposalId), Is.True);

        // Act — edit the proposal with new values
        var editedJson = JsonSerializer.Serialize(new
        {
            name = "Silver Key of Voss",
            type = "Item",
            summary = "An ornate silver key found in Captain Voss's quarters",
            visibility = "PartyVisible",
            confidence = 0.95
        });

        var editRequest = new EditProposalRequest(editedJson);
        var editResponse = await _scenario.GmClient.PostAsJsonAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/edit", editRequest);
        Assert.That(editResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var editResult = await editResponse.Content.ReadFromJsonAsync<EditProposalResponse>();
        Assert.That(editResult, Is.Not.Null);
        Assert.That(editResult!.Status, Is.EqualTo("Edited"));
        Assert.That(editResult.ProposedValueJson, Is.EqualTo(editedJson));

        // Act — accept the edited proposal
        var acceptResponse = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/accept", null);
        Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AcceptProposalResponse>();
        Assert.That(acceptResult, Is.Not.Null);
        Assert.That(acceptResult!.CreatedEntityId, Is.Not.Null);

        // Assert — the artifact uses the edited values, not the original
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var artifact = db.Artifacts.FirstOrDefault(a => a.Id == acceptResult.CreatedEntityId);
        Assert.That(artifact, Is.Not.Null);
        Assert.That(artifact!.Name, Is.EqualTo("Silver Key of Voss"));
        Assert.That(artifact.Summary, Is.EqualTo("An ornate silver key found in Captain Voss's quarters"));
        Assert.That(artifact.Confidence, Is.EqualTo(0.95m));
    }

    #endregion

    #region Batch Accept — Correct Succeeded/Failed Partition

    [Test]
    public async Task BatchAccept_ReturnsCorrectSucceededFailedPartition()
    {
        // Arrange — seed multiple proposals: some valid, one already rejected (will fail)
        var validProposal1 = await SeedCreateArtifactProposalAsync(
            name: "Missing Caravan", type: "Event", summary: "A caravan disappeared");
        var validProposal2 = await SeedCreateArtifactProposalAsync(
            name: "Harbor District", type: "Location", summary: "The waterfront area");

        // Create a proposal that is already rejected (will report as failed — conflicting state)
        var rejectedProposalId = await SeedCreateArtifactProposalAsync(
            name: "Phantom Entity", type: "Character", summary: "Already rejected");
        // Reject it first
        await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{rejectedProposalId}/reject", null);

        var nonExistentId = Guid.NewGuid();

        // Act — batch accept all four IDs
        var batchRequest = new BatchAcceptRequest(
            new List<Guid> { validProposal1, validProposal2, rejectedProposalId, nonExistentId });

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"{ReviewsUrl}/proposals/batch-accept", batchRequest);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var batchResult = await response.Content.ReadFromJsonAsync<BatchOperationResponse>();
        Assert.That(batchResult, Is.Not.Null);

        // Assert — valid proposals succeeded
        Assert.That(batchResult!.Succeeded, Does.Contain(validProposal1));
        Assert.That(batchResult.Succeeded, Does.Contain(validProposal2));

        // Assert — rejected proposal and non-existent proposal failed
        Assert.That(batchResult.Failed.Any(f => f.ProposalId == rejectedProposalId), Is.True);
        Assert.That(batchResult.Failed.Any(f => f.ProposalId == nonExistentId), Is.True);
    }

    #endregion

    #region Batch Reject — Correct Succeeded/Failed Partition

    [Test]
    public async Task BatchReject_ReturnsCorrectSucceededFailedPartition()
    {
        // Arrange — seed proposals: some valid pending, one already accepted (will fail)
        var validProposal1 = await SeedCreateArtifactProposalAsync(
            name: "Tavrin", type: "Character", summary: "A rogue with a past");
        var validProposal2 = await SeedCreateArtifactProposalAsync(
            name: "Dockside Tavern", type: "Location", summary: "Where rumors flow");

        // Create a proposal that is already accepted (will report as failed — conflicting state)
        var acceptedProposalId = await SeedCreateArtifactProposalAsync(
            name: "Already Accepted", type: "Character", summary: "Already done");
        await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{acceptedProposalId}/accept", null);

        var nonExistentId = Guid.NewGuid();

        // Act — batch reject
        var batchRequest = new BatchRejectRequest(
            new List<Guid> { validProposal1, validProposal2, acceptedProposalId, nonExistentId });

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"{ReviewsUrl}/proposals/batch-reject", batchRequest);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var batchResult = await response.Content.ReadFromJsonAsync<BatchOperationResponse>();
        Assert.That(batchResult, Is.Not.Null);

        // Assert — valid proposals succeeded
        Assert.That(batchResult!.Succeeded, Does.Contain(validProposal1));
        Assert.That(batchResult.Succeeded, Does.Contain(validProposal2));

        // Assert — accepted proposal and non-existent proposal failed
        Assert.That(batchResult.Failed.Any(f => f.ProposalId == acceptedProposalId), Is.True);
        Assert.That(batchResult.Failed.Any(f => f.ProposalId == nonExistentId), Is.True);
    }

    #endregion

    #region Visibility Enforcement — Invisible Proposal Returns 404

    [Test]
    public async Task VisibilityEnforcement_InvisibleProposal_Returns404()
    {
        // Arrange — seed a proposal from a GMOnly source (invisible to player)
        var gmOnlyProposalId = await SeedProposalFromGmOnlySourceAsync();

        // Act — player tries to accept a proposal they cannot see
        var response = await _scenario.PlayerClient.PostAsync(
            $"{ReviewsUrl}/proposals/{gmOnlyProposalId}/accept", null);

        // Assert — not found (not forbidden), per visibility enforcement rules
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task VisibilityEnforcement_InvisibleProposal_RejectReturns404()
    {
        // Arrange — seed a proposal from a GMOnly source
        var gmOnlyProposalId = await SeedProposalFromGmOnlySourceAsync();

        // Act — player tries to reject a proposal they cannot see
        var response = await _scenario.PlayerClient.PostAsync(
            $"{ReviewsUrl}/proposals/{gmOnlyProposalId}/reject", null);

        // Assert — not found
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion

    #region Idempotent Accept — Returns 200 With Existing Data

    [Test]
    public async Task IdempotentAccept_AlreadyAccepted_Returns200WithExistingData()
    {
        // Arrange — seed and accept a proposal
        var proposalId = await SeedCreateArtifactProposalAsync(
            name: "The Docks", type: "Location", summary: "Where ships are moored");

        var firstAcceptResponse = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/accept", null);
        Assert.That(firstAcceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var firstResult = await firstAcceptResponse.Content.ReadFromJsonAsync<AcceptProposalResponse>();

        // Act — accept the same proposal again (idempotent retry)
        var secondAcceptResponse = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/accept", null);

        // Assert — returns 200 with the same data, not an error
        Assert.That(secondAcceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var secondResult = await secondAcceptResponse.Content.ReadFromJsonAsync<AcceptProposalResponse>();
        Assert.That(secondResult, Is.Not.Null);
        Assert.That(secondResult!.ProposalId, Is.EqualTo(proposalId));
        Assert.That(secondResult.Status, Is.EqualTo("Accepted"));
        Assert.That(secondResult.ReviewedAt, Is.EqualTo(firstResult!.ReviewedAt));
        Assert.That(secondResult.ReviewedByUserId, Is.EqualTo(firstResult.ReviewedByUserId));

        // Verify no duplicate artifacts were created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var artifactCount = db.Artifacts.Count(a => a.Name == "The Docks");
        Assert.That(artifactCount, Is.EqualTo(1));
    }

    #endregion

    #region Conflicting State — Returns 409

    [Test]
    public async Task ConflictingState_AcceptRejectedProposal_Returns409()
    {
        // Arrange — seed and reject a proposal
        var proposalId = await SeedCreateArtifactProposalAsync(
            name: "Ghost Ship", type: "Item", summary: "A spectral vessel");

        await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/reject", null);

        // Act — try to accept the rejected proposal
        var response = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/accept", null);

        // Assert — 409 Conflict
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task ConflictingState_RejectAcceptedProposal_Returns409()
    {
        // Arrange — seed and accept a proposal
        var proposalId = await SeedCreateArtifactProposalAsync(
            name: "Merchant Guild", type: "Faction", summary: "Controls the trade routes");

        await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/accept", null);

        // Act — try to reject the accepted proposal
        var response = await _scenario.GmClient.PostAsync(
            $"{ReviewsUrl}/proposals/{proposalId}/reject", null);

        // Assert — 409 Conflict
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Seeds a CreateArtifact proposal into the database linked to the GM's source and review batch.
    /// </summary>
    private async Task<Guid> SeedCreateArtifactProposalAsync(
        string name,
        string type,
        string summary,
        string? visibility = null,
        decimal? confidence = null)
    {
        var proposedValueJson = JsonSerializer.Serialize(new
        {
            name,
            type,
            summary,
            visibility = visibility ?? "PartyVisible",
            confidence = confidence ?? 0.8m
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _scenario.ReviewBatch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = proposedValueJson,
            Rationale = $"Extracted from session notes about {name}",
            Confidence = confidence ?? 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ReviewProposals.Add(proposal);
        await db.SaveChangesAsync();
        return proposal.Id;
    }

    /// <summary>
    /// Seeds a proposal from a GMOnly source that should be invisible to the player.
    /// </summary>
    private async Task<Guid> SeedProposalFromGmOnlySourceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        // Create a GMOnly source owned by the GM
        var gmOnlySource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            CreatedByUserId = _scenario.GmUserId,
            Title = "Secret GM Notes",
            Type = SourceType.GMNote,
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Sources.Add(gmOnlySource);

        // Create a review batch for this GMOnly source
        var gmOnlyBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            SourceId = gmOnlySource.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewBatches.Add(gmOnlyBatch);

        // Create a proposal in this batch
        var proposedValueJson = JsonSerializer.Serialize(new
        {
            name = "Hidden NPC",
            type = "Character",
            summary = "A secret ally known only to the GM",
            visibility = "GMOnly",
            confidence = 0.9
        });

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = gmOnlyBatch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = proposedValueJson,
            Rationale = "GM-only entity from secret notes",
            Confidence = 0.9m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewProposals.Add(proposal);

        await db.SaveChangesAsync();
        return proposal.Id;
    }

    /// <summary>
    /// Sets up the full review test scenario: world, users, members, source, and review batch.
    /// </summary>
    private static async Task<ReviewTestScenario> SetupReviewScenarioAsync(
        NornisWebApplicationFactory factory)
    {
        // Provision users via authenticated requests (triggering UserProvisioningMiddleware)
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-kelda-review", "kelda@blackharbor.com", "Kelda");

        var playerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|player-tavrin-review", "tavrin@blackharbor.com", "Tavrin");

        var observerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|observer-jorin-review", "jorin@blackharbor.com", "Jorin");

        // Create world with GM
        var world = await SourceTestHelpers.CreateTestWorldAsync(factory, gmUserId);

        // Add player and observer members
        await SourceTestHelpers.AddWorldMemberAsync(
            factory, world.Id, playerUserId, WorldRole.Player,
            displayName: "Tavrin", characterName: "Tavrin the Bold");

        await SourceTestHelpers.AddWorldMemberAsync(
            factory, world.Id, observerUserId, WorldRole.Observer,
            displayName: "Jorin");

        // Create a PartyVisible source owned by the GM for seeding proposals
        var gmSource = await SourceTestHelpers.CreateTestSourceAsync(
            factory, world.Id, gmUserId,
            title: "Session 4 — Questioning Captain Voss",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Processed);

        // Create a review batch for this source
        ReviewBatch reviewBatch;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            reviewBatch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                SourceId = gmSource.Id,
                Status = ReviewBatchStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ReviewBatches.Add(reviewBatch);
            await db.SaveChangesAsync();
        }

        return new ReviewTestScenario
        {
            World = world,
            GmUserId = gmUserId,
            PlayerUserId = playerUserId,
            ObserverUserId = observerUserId,
            GmSource = gmSource,
            ReviewBatch = reviewBatch,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-kelda-review", email: "kelda@blackharbor.com", nickname: "Kelda"),
            PlayerClient = factory.CreateAuthenticatedClient(
                sub: "auth0|player-tavrin-review", email: "tavrin@blackharbor.com", nickname: "Tavrin"),
            ObserverClient = factory.CreateAuthenticatedClient(
                sub: "auth0|observer-jorin-review", email: "jorin@blackharbor.com", nickname: "Jorin")
        };
    }

    #endregion
}

/// <summary>
/// Contains all entities and pre-configured HTTP clients for review workflow integration tests.
/// </summary>
public class ReviewTestScenario
{
    public required World World { get; init; }
    public required Guid GmUserId { get; init; }
    public required Guid PlayerUserId { get; init; }
    public required Guid ObserverUserId { get; init; }
    public required Source GmSource { get; init; }
    public required ReviewBatch ReviewBatch { get; init; }
    public required HttpClient GmClient { get; init; }
    public required HttpClient PlayerClient { get; init; }
    public required HttpClient ObserverClient { get; init; }
}
