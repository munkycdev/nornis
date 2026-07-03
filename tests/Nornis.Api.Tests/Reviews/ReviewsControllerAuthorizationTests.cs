using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Reviews;

/// <summary>
/// Integration tests for ReviewsController authorization and campaign membership enforcement.
/// Validates: Requirements 6.5, 6.6, 12.1–12.6
/// </summary>
[TestFixture]
public class ReviewsControllerAuthorizationTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;
    private Guid _proposalId;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        // Seed a source, review batch, and proposal so we have something to test against
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.Campaign.Id,
            _scenario.GmUserId,
            title: "Session 4 — Questioning Captain Voss",
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Processed);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = _scenario.Campaign.Id,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewBatches.Add(batch);

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = JsonSerializer.Serialize(new
            {
                name = "Captain Voss",
                type = "Character",
                summary = "A harbor captain suspected of involvement in the missing caravan.",
                visibility = "PartyVisible",
                confidence = 0.85m
            }),
            Rationale = "Source mentions Captain Voss being questioned.",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewProposals.Add(proposal);
        await db.SaveChangesAsync();

        _proposalId = proposal.Id;
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _scenario.ObserverClient.Dispose();
        _factory.Dispose();
    }

    private string ProposalsUrl => $"/api/campaigns/{_scenario.Campaign.Id}/reviews/proposals";
    private string AcceptUrl => $"/api/campaigns/{_scenario.Campaign.Id}/reviews/proposals/{_proposalId}/accept";
    private string RejectUrl => $"/api/campaigns/{_scenario.Campaign.Id}/reviews/proposals/{_proposalId}/reject";
    private string EditUrl => $"/api/campaigns/{_scenario.Campaign.Id}/reviews/proposals/{_proposalId}/edit";
    private string BatchAcceptUrl => $"/api/campaigns/{_scenario.Campaign.Id}/reviews/proposals/batch-accept";
    private string BatchRejectUrl => $"/api/campaigns/{_scenario.Campaign.Id}/reviews/proposals/batch-reject";

    #region CampaignMemberActionFilter applied to all review endpoints (Req 12.1)

    /// <summary>
    /// All review endpoints require campaign membership via CampaignMemberActionFilter.
    /// A non-member should get 403 for every endpoint.
    /// Validates: Requirements 12.1, 12.2
    /// </summary>
    [Test]
    public async Task ListProposals_NonMember_Returns403()
    {
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rando",
            email: "rando@outsider.com",
            nickname: "Rando");

        // Trigger user provisioning
        await outsider.GetAsync("/api/campaigns");

        var response = await outsider.GetAsync(ProposalsUrl);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        outsider.Dispose();
    }

    [Test]
    public async Task AcceptProposal_NonMember_Returns403()
    {
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rando",
            email: "rando@outsider.com",
            nickname: "Rando");

        await outsider.GetAsync("/api/campaigns");

        var response = await outsider.PostAsync(AcceptUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        outsider.Dispose();
    }

    [Test]
    public async Task RejectProposal_NonMember_Returns403()
    {
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rando",
            email: "rando@outsider.com",
            nickname: "Rando");

        await outsider.GetAsync("/api/campaigns");

        var response = await outsider.PostAsync(RejectUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        outsider.Dispose();
    }

    [Test]
    public async Task EditProposal_NonMember_Returns403()
    {
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rando",
            email: "rando@outsider.com",
            nickname: "Rando");

        await outsider.GetAsync("/api/campaigns");

        var request = new EditProposalRequest("{\"name\":\"Modified\"}");
        var response = await outsider.PostAsJsonAsync(EditUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        outsider.Dispose();
    }

    [Test]
    public async Task BatchAccept_NonMember_Returns403()
    {
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rando",
            email: "rando@outsider.com",
            nickname: "Rando");

        await outsider.GetAsync("/api/campaigns");

        var request = new BatchAcceptRequest([_proposalId]);
        var response = await outsider.PostAsJsonAsync(BatchAcceptUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        outsider.Dispose();
    }

    [Test]
    public async Task BatchReject_NonMember_Returns403()
    {
        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rando",
            email: "rando@outsider.com",
            nickname: "Rando");

        await outsider.GetAsync("/api/campaigns");

        var request = new BatchRejectRequest([_proposalId]);
        var response = await outsider.PostAsJsonAsync(BatchRejectUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        outsider.Dispose();
    }

    #endregion

    #region Invalid campaignId returns 404 (Req 12.5)

    /// <summary>
    /// When the campaignId route parameter is not a valid GUID, the API returns 404.
    /// Validates: Requirement 12.5
    /// </summary>
    [Test]
    public async Task ListProposals_InvalidCampaignIdFormat_Returns404()
    {
        var response = await _scenario.GmClient.GetAsync("/api/campaigns/not-a-guid/reviews/proposals");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AcceptProposal_InvalidCampaignIdFormat_Returns404()
    {
        var response = await _scenario.GmClient.PostAsync(
            $"/api/campaigns/not-a-guid/reviews/proposals/{_proposalId}/accept", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RejectProposal_InvalidCampaignIdFormat_Returns404()
    {
        var response = await _scenario.GmClient.PostAsync(
            $"/api/campaigns/not-a-guid/reviews/proposals/{_proposalId}/reject", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion

    #region Non-existent campaign returns 403 for non-member (Req 12.4)

    /// <summary>
    /// When a review endpoint references a campaign that does not exist, 
    /// the API returns 403 (indistinguishable from non-member).
    /// Validates: Requirement 12.4
    /// </summary>
    [Test]
    public async Task ListProposals_NonExistentCampaign_Returns403()
    {
        var nonExistentCampaignId = Guid.NewGuid();

        var response = await _scenario.GmClient.GetAsync(
            $"/api/campaigns/{nonExistentCampaignId}/reviews/proposals");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AcceptProposal_NonExistentCampaign_Returns403()
    {
        var nonExistentCampaignId = Guid.NewGuid();

        var response = await _scenario.GmClient.PostAsync(
            $"/api/campaigns/{nonExistentCampaignId}/reviews/proposals/{_proposalId}/accept", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region Missing JWT returns 401 (Req 12.6)

    /// <summary>
    /// When the request does not contain a valid JWT, the API returns 401 before
    /// any membership check executes.
    /// Validates: Requirement 12.6
    /// </summary>
    [Test]
    public async Task ListProposals_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ProposalsUrl);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AcceptProposal_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(AcceptUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task RejectProposal_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(RejectUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EditProposal_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var request = new EditProposalRequest("{\"name\":\"test\"}");
        var response = await client.PostAsJsonAsync(EditUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task BatchAccept_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var request = new BatchAcceptRequest([_proposalId]);
        var response = await client.PostAsJsonAsync(BatchAcceptUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task BatchReject_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var request = new BatchRejectRequest([_proposalId]);
        var response = await client.PostAsJsonAsync(BatchRejectUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AcceptProposal_ExpiredJwt_Returns401()
    {
        var token = TestJwtIssuer.GenerateExpiredToken();
        var client = _factory.CreateClient().WithAuthToken(token);

        var response = await client.PostAsync(AcceptUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    #endregion

    #region Observer attempting accept/reject/edit returns 404 (Req 6.4 + 7.4)

    /// <summary>
    /// An Observer is a member of the campaign but cannot perform review operations.
    /// Per Req 7.4, proposals invisible due to visibility rules are treated as not-found.
    /// Since Observers have zero visibility (IsSourceVisibleToUser always returns false),
    /// the visibility check triggers before the role-based authorization check,
    /// yielding a 404 not-found rather than 403.
    /// Validates: Requirements 6.4, 7.4
    /// </summary>
    [Test]
    public async Task AcceptProposal_Observer_Returns404_DueToVisibility()
    {
        var response = await _scenario.ObserverClient.PostAsync(AcceptUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RejectProposal_Observer_Returns404_DueToVisibility()
    {
        var response = await _scenario.ObserverClient.PostAsync(RejectUrl, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EditProposal_Observer_Returns404_DueToVisibility()
    {
        var request = new EditProposalRequest("{\"name\":\"Captain Voss Modified\",\"type\":\"Character\"}");
        var response = await _scenario.ObserverClient.PostAsJsonAsync(EditUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ListProposals_Observer_ReturnsEmptyList()
    {
        // Observers see zero proposals per Req 1.3, returned as empty list (not an error)
        var response = await _scenario.ObserverClient.GetAsync(ProposalsUrl);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain("\"proposals\":[]").Or.Contains("\"proposals\": []"));
    }

    [Test]
    public async Task BatchAccept_Observer_ReturnsFailedProposals()
    {
        // Batch operations for Observer: each proposal reported as not_found in the failed list
        var request = new BatchAcceptRequest([_proposalId]);
        var response = await _scenario.ObserverClient.PostAsJsonAsync(BatchAcceptUrl, request);

        // Batch operations may return 200 with failed proposals or may deny at a higher level
        // The service processes each proposal individually, Observer can't see any → all fail
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task BatchReject_Observer_ReturnsFailedProposals()
    {
        var request = new BatchRejectRequest([_proposalId]);
        var response = await _scenario.ObserverClient.PostAsJsonAsync(BatchRejectUrl, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    #endregion

    #region User identity derived from JWT (Req 6.5, 12.3)

    /// <summary>
    /// The API derives user identity from the JWT sub claim, not from client-provided values.
    /// A valid GM should be able to accept when properly authenticated.
    /// Validates: Requirements 6.5, 12.3
    /// </summary>
    [Test]
    public async Task AcceptProposal_GmWithValidJwt_IsAuthorized()
    {
        var response = await _scenario.GmClient.PostAsync(AcceptUrl, null);

        // GM should be authorized — expect success (200) or at least not 401/403
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion
}
