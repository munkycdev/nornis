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

    private string HealthUrl => $"/api/worlds/{_scenario.World.Id}/health";

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
            .Where(r => r.WorldId == _scenario.World.Id
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

    [Test]
    public async Task GetAssessment_AfterEditingCitedFact_MarksFindingStaleAndSuspendsPenalty()
    {
        var factId = await GetVossFactIdAsync();
        _factory.FakeAuditAiClient.SetupFindings(new AuditFinding
        {
            Category = "Contradiction",
            Severity = "High",
            Summary = "Voss's stated location conflicts with the harbor records.",
            SuggestedAction = "Reconcile the two location facts.",
            Evidence = [$"fact:{factId}"],
            ArtifactRef = $"artifact:{_scenario.Voss.Id}"
        });

        var assessResponse = await _scenario.GmClient.PostAsync($"{HealthUrl}/assess", content: null);
        var assessed = await assessResponse.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();
        var finding = assessed!.Findings[0];
        Assert.That(finding.IsStale, Is.False);
        Assert.That(finding.EvidenceItems, Has.Count.EqualTo(1));
        Assert.That(finding.EvidenceItems[0].Kind, Is.EqualTo("Fact"));
        Assert.That(finding.EvidenceItems[0].ArtifactId, Is.EqualTo(_scenario.Voss.Id));
        Assert.That(finding.EvidenceItems[0].Label, Does.Contain("Captain Voss"));

        // The GM edits the cited fact after the audit ran.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var fact = db.ArtifactFacts.Single(f => f.Id == factId);
            fact.Value = "Aboard the Grey Gull";
            fact.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        var afterResponse = await _scenario.GmClient.GetAsync($"{HealthUrl}/assessment");
        var after = await afterResponse.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();

        // The finding is stale, its evidence item is flagged, and its penalty is suspended.
        Assert.That(after!.Findings[0].IsStale, Is.True);
        Assert.That(after.Findings[0].Status, Is.EqualTo("Open"));
        Assert.That(after.Findings[0].EvidenceItems[0].ChangedSinceAudit, Is.True);
        Assert.That(after.EffectiveScore, Is.EqualTo(assessed.EffectiveScore + 12));
    }

    [Test]
    public async Task GetAssessment_AfterDeletingCitedFact_MarksEvidenceMissing()
    {
        var factId = await GetVossFactIdAsync();
        _factory.FakeAuditAiClient.SetupFindings(new AuditFinding
        {
            Category = "DanglingThread",
            Severity = "Medium",
            Summary = "The harbor location is never followed up.",
            SuggestedAction = null,
            Evidence = [$"fact:{factId}"],
            ArtifactRef = null
        });

        _ = await _scenario.GmClient.PostAsync($"{HealthUrl}/assess", content: null);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            db.ArtifactFacts.Remove(db.ArtifactFacts.Single(f => f.Id == factId));
            await db.SaveChangesAsync();
        }

        var response = await _scenario.GmClient.GetAsync($"{HealthUrl}/assessment");
        var payload = await response.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();

        Assert.That(payload!.Findings[0].IsStale, Is.True);
        Assert.That(payload.Findings[0].EvidenceItems[0].Missing, Is.True);
        Assert.That(payload.Findings[0].EvidenceItems[0].ArtifactId, Is.Null);
    }

    [Test]
    public async Task DraftFix_RoundTrip_CreatesPendingProposalsInReviewQueue()
    {
        var factId = await GetVossFactIdAsync();
        _factory.FakeAuditAiClient.SetupFindings(new AuditFinding
        {
            Category = "Contradiction",
            Severity = "High",
            Summary = "Voss's stated location conflicts with the harbor records.",
            SuggestedAction = "Reconcile the two location facts.",
            Evidence = [$"fact:{factId}"],
            ArtifactRef = $"artifact:{_scenario.Voss.Id}"
        });

        var assessResponse = await _scenario.GmClient.PostAsync($"{HealthUrl}/assess", content: null);
        var assessed = await assessResponse.Content.ReadFromJsonAsync<ContinuityAssessmentResponse>();
        var findingId = assessed!.Findings[0].Id;

        _factory.FakeFixAiClient.SetupProposals(
            new ContinuityFixProposal
            {
                ChangeType = "UpdateFact",
                TargetRef = $"[ref:fact:{factId}]",
                Rationale = "Retire the harbor location — the record supports the ship sighting.",
                TruthState = "False"
            },
            new ContinuityFixProposal
            {
                ChangeType = "UpdateFact",
                TargetRef = $"fact:{Guid.NewGuid()}",
                Rationale = "Ungrounded target — must be dropped.",
                Value = "anything"
            });

        var draftResponse = await _scenario.GmClient.PostAsync(
            $"{HealthUrl}/findings/{findingId}/draft-fix", content: null);
        Assert.That(draftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var draft = await draftResponse.Content.ReadFromJsonAsync<DraftFixResponse>();
        Assert.That(draft!.ProposalCount, Is.EqualTo(1));
        Assert.That(draft.BatchId, Is.Not.Null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var batch = db.ReviewBatches.Single(b => b.Id == draft.BatchId);
        Assert.That(batch.Kind, Is.EqualTo("ContinuityFix"));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));

        var proposals = db.ReviewProposals.Where(p => p.ReviewBatchId == batch.Id).ToList();
        Assert.That(proposals, Has.Count.EqualTo(1));
        Assert.That(proposals[0].ChangeType, Is.EqualTo(ReviewChangeType.UpdateFact));
        Assert.That(proposals[0].TargetId, Is.EqualTo(factId));
        Assert.That(proposals[0].Status, Is.EqualTo(ReviewProposalStatus.Pending));
        Assert.That(proposals[0].ProposedValueJson, Does.Contain("\"truthState\":\"False\""));

        var source = db.Sources.Single(s => s.Id == draft.SourceId);
        Assert.That(source.Type, Is.EqualTo(SourceType.GMNote));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly));

        var usage = db.AiUsageRecords
            .Where(r => r.WorldId == _scenario.World.Id
                        && r.OperationType == AiOperationType.ContinuityFix)
            .ToList();
        Assert.That(usage, Has.Count.EqualTo(1));
        Assert.That(usage[0].Succeeded, Is.True);
    }

    [Test]
    public async Task DraftFix_NonGm_Returns403()
    {
        var response = await _scenario.PlayerClient.PostAsync(
            $"{HealthUrl}/findings/{Guid.NewGuid()}/draft-fix", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.FakeFixAiClient.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_UnknownFinding_Returns404()
    {
        var response = await _scenario.GmClient.PostAsync(
            $"{HealthUrl}/findings/{Guid.NewGuid()}/draft-fix", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<Guid> GetVossFactIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        return await Task.FromResult(db.ArtifactFacts.Single(f => f.ArtifactId == _scenario.Voss.Id).Id);
    }

    private static async Task<ContinuityAuditScenario> SetupScenarioAsync(ContinuityAuditTestFactory factory)
    {
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-continuity", "gm@continuity.test", "Kelda");
        var playerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|player-continuity", "player@continuity.test", "Tavrin");

        var world = await SourceTestHelpers.CreateTestWorldAsync(factory, gmUserId);
        await SourceTestHelpers.AddWorldMemberAsync(
            factory, world.Id, playerUserId, WorldRole.Player, displayName: "Tavrin");

        Artifact voss;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

            voss = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
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
            World = world,
            Voss = voss,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-continuity", email: "gm@continuity.test", nickname: "Kelda"),
            PlayerClient = factory.CreateAuthenticatedClient(
                sub: "auth0|player-continuity", email: "player@continuity.test", nickname: "Tavrin")
        };
    }
}

/// <summary>Factory that swaps the audit and fix AI clients for fakes and prices their model.</summary>
public class ContinuityAuditTestFactory : NornisWebApplicationFactory
{
    public FakeAuditAiClient FakeAuditAiClient { get; } = new();
    public FakeContinuityFixAiClient FakeFixAiClient { get; } = new();

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

            var fixDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IContinuityFixAiClient));
            if (fixDescriptor is not null)
            {
                services.Remove(fixDescriptor);
            }

            services.AddSingleton<IContinuityFixAiClient>(FakeFixAiClient);
        });
    }
}

public class ContinuityAuditScenario
{
    public required World World { get; init; }
    public required Artifact Voss { get; init; }
    public required HttpClient GmClient { get; init; }
    public required HttpClient PlayerClient { get; init; }
}
