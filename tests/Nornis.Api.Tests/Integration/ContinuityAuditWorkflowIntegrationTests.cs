using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Integration;

/// <summary>
/// Integration tests for the AI-assessed Continuity Health endpoints through HTTP: manual assess,
/// get-latest, dismiss, GM-only authorization, and the empty (never-assessed) state. The audit AI
/// client is replaced with a fake so no live Azure OpenAI call is made.
/// </summary>
[TestFixture]
public class ContinuityAuditWorkflowIntegrationTests
{
    private ContinuityAuditTestFactory _factory = null!;
    private ContinuityAuditScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ContinuityAuditTestFactory();
        _ = _factory.CreateClient();
        _scenario = await SetupScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _factory.Dispose();
    }

    private string HealthUrl => $"/api/campaigns/{_scenario.Campaign.Id}/health";

    [Test]
    public async Task Assess_Get_Dismiss_RoundTrip()
    {
        _factory.FakeAuditAiClient.SetupFindings(new AuditFinding
        {
            Category = "Contradiction",
            Severity = "High",
            Summary = "Voss's stated location conflicts with the harbor records.",
            SuggestedAction = "Reconcile the two location facts.",
            Evidence = [$"artifact:{_scenario.Voss.Id}"],
            ArtifactRef = $"artifact:{_scenario.Voss.Id}"
        });

        // 1. Manual assess.
        var assessResponse = await _scenario.GmClient.PostAsync($"{HealthUrl}/assess", content: null);
        Assert.That(assessResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var assessed = await assessResponse.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();
        Assert.That(assessed, Is.Not.Null);
        Assert.That(assessed!.HasData, Is.True);
        Assert.That(assessed.Findings, Has.Count.EqualTo(1));
        var finding = assessed.Findings[0];
        Assert.That(finding.Category, Is.EqualTo("Contradiction"));
        Assert.That(finding.Severity, Is.EqualTo("High"));
        Assert.That(finding.ArtifactId, Is.EqualTo(_scenario.Voss.Id));
        Assert.That(finding.Status, Is.EqualTo("Open"));

        var effectiveBefore = assessed.EffectiveScore;

        // 2. Get latest.
        var getResponse = await _scenario.GmClient.GetAsync($"{HealthUrl}/assessment");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var latest = await getResponse.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();
        Assert.That(latest!.HasData, Is.True);
        Assert.That(latest.Findings, Has.Count.EqualTo(1));
        Assert.That(latest.AssessmentId, Is.EqualTo(assessed.AssessmentId));

        // 3. Dismiss the finding.
        var dismissResponse = await _scenario.GmClient.PostAsync(
            $"{HealthUrl}/findings/{finding.Id}/dismiss", content: null);
        Assert.That(dismissResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 4. Get again — the finding is dismissed and the effective score has recovered.
        var afterResponse = await _scenario.GmClient.GetAsync($"{HealthUrl}/assessment");
        var after = await afterResponse.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();
        Assert.That(after!.Findings[0].Status, Is.EqualTo("Dismissed"));
        Assert.That(after.EffectiveScore, Is.EqualTo(effectiveBefore + 12));

        // A usage record was written for the audit call.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var usage = db.AiUsageRecords
            .Where(r => r.CampaignId == _scenario.Campaign.Id
                        && r.OperationType == AiOperationType.ContinuityAudit)
            .ToList();
        Assert.That(usage, Has.Count.EqualTo(1));
        Assert.That(usage[0].Succeeded, Is.True);
    }

    [Test]
    public async Task GetAssessment_NoAssessmentYet_ReturnsHasDataFalse()
    {
        var response = await _scenario.GmClient.GetAsync($"{HealthUrl}/assessment");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var payload = await response.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();
        Assert.That(payload!.HasData, Is.False);
        Assert.That(payload.Findings, Is.Empty);
    }

    [Test]
    public async Task Assess_NonGm_Returns403()
    {
        var response = await _scenario.PlayerClient.PostAsync($"{HealthUrl}/assess", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.FakeAuditAiClient.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetAssessment_NonGm_Returns403()
    {
        var response = await _scenario.PlayerClient.GetAsync($"{HealthUrl}/assessment");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static async Task<ContinuityAuditScenario> SetupScenarioAsync(ContinuityAuditTestFactory factory)
    {
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-continuity", "gm@continuity.test", "Kelda");
        var playerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|player-continuity", "player@continuity.test", "Tavrin");

        var campaign = await SourceTestHelpers.CreateTestCampaignAsync(factory, gmUserId);
        await SourceTestHelpers.AddCampaignMemberAsync(
            factory, campaign.Id, playerUserId, CampaignRole.Player, displayName: "Tavrin");

        Artifact voss;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

            voss = new Artifact
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                Type = ArtifactType.Character,
                Name = "Captain Voss",
                Summary = "A harbor captain suspected of smuggling.",
                Visibility = VisibilityScope.PartyVisible,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(voss);

            db.ArtifactFacts.Add(new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = voss.Id,
                Predicate = "location",
                Value = "Black Harbor",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        return new ContinuityAuditScenario
        {
            Campaign = campaign,
            Voss = voss,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-continuity", email: "gm@continuity.test", nickname: "Kelda"),
            PlayerClient = factory.CreateAuthenticatedClient(
                sub: "auth0|player-continuity", email: "player@continuity.test", nickname: "Tavrin")
        };
    }
}

/// <summary>Factory that swaps the audit AI client for a fake and prices the fake's model.</summary>
public class ContinuityAuditTestFactory : NornisWebApplicationFactory
{
    public FakeAuditAiClient FakeAuditAiClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Loremaster:ModelPricing:gpt-4o:InputPerMillionTokensUsd"] = "2.50",
                ["Loremaster:ModelPricing:gpt-4o:OutputPerMillionTokensUsd"] = "10.00",
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAuditAiClient));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IAuditAiClient>(FakeAuditAiClient);
        });
    }
}

public class ContinuityAuditScenario
{
    public required Campaign Campaign { get; init; }
    public required Artifact Voss { get; init; }
    public required HttpClient GmClient { get; init; }
    public required HttpClient PlayerClient { get; init; }
}
