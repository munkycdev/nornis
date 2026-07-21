using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ContinuityAuditServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private FakeAuditAiClient _ai = null!;
    private InMemoryHealthAssessmentRepository _assessmentRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private ContinuityAuditService _service = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;

    private Guid _worldId;
    private Artifact _voss = null!;
    private ArtifactFact _vossFact = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();
        _ai = new FakeAuditAiClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _assessmentRepo = new InMemoryHealthAssessmentRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();

        var health = new HealthService(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo);
        var options = Options.Create(new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new() { InputPerMillionTokensUsd = 2.5m, OutputPerMillionTokensUsd = 10m }
            }
        });

        _service = new ContinuityAuditService(
            _budgetGuard,
            health, _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo,
            _ai, _assessmentRepo, _usageRepo, options);

        _worldId = Guid.NewGuid();

        _voss = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Summary = "A harbor captain.",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(_voss);

        _vossFact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = _voss.Id,
            Predicate = "location",
            Value = "Black Harbor",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _factRepo.Seed(_vossFact);
    }

    private string ArtifactRef => $"artifact:{_voss.Id}";
    private string FactRef => $"fact:{_vossFact.Id}";

    private AuditFinding Finding(
        string category = "Contradiction",
        string severity = "High",
        string summary = "Voss is in two places at once.",
        string? action = "Reconcile the location facts.",
        IReadOnlyList<string>? evidence = null,
        string? artifactRef = null) =>
        new()
        {
            Category = category,
            Severity = severity,
            Summary = summary,
            SuggestedAction = action,
            Evidence = evidence ?? [FactRef],
            ArtifactRef = artifactRef
        };

    [Test]
    public async Task RunAssessment_BudgetExceeded_Returns429WithoutCallingAi()
    {
        _budgetGuard.Exceeded = true;

        var result = await _service.RunAssessmentAsync(_worldId, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(429));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAssessment_PersistsFindings()
    {
        _ai.SetupFindings(
            Finding(evidence: [ArtifactRef]),
            Finding(category: "DanglingThread", severity: "Low", evidence: [FactRef]));

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Findings, Has.Count.EqualTo(2));
        Assert.That(_assessmentRepo.Assessments, Has.Count.EqualTo(1));
        Assert.That(_assessmentRepo.Findings, Has.Count.EqualTo(2));
        Assert.That(_assessmentRepo.Findings.All(f => f.Status == ContinuityFindingStatus.Open), Is.True);
    }

    [Test]
    public async Task RunAssessment_ResolvesArtifactIdFromFactEvidence()
    {
        _ai.SetupFindings(Finding(evidence: [FactRef]));

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        // The finding cites a fact — the primary artifact should resolve to that fact's owner.
        Assert.That(result.Value!.Findings[0].ArtifactId, Is.EqualTo(_voss.Id));
    }

    [Test]
    public async Task RunAssessment_EvidenceWithRefPrefix_AsTheModelActuallyReturnsIt_Resolves()
    {
        // The record renders ids as [ref:fact:GUID] and the prompt says to copy them exactly,
        // so the real model echoes the "ref:" prefix. Regression: these must still resolve.
        _ai.SetupFindings(Finding(
            evidence: [$"ref:{FactRef}", $"[ref:{ArtifactRef}]"],
            artifactRef: $"ref:{ArtifactRef}"));

        var result = await _service.RunAssessmentAsync(_worldId, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Findings, Has.Count.EqualTo(1));
        Assert.That(result.Value.Findings[0].ArtifactId, Is.EqualTo(_voss.Id));
    }

    [Test]
    public async Task RunAssessment_DropsFindingsWithUnresolvableEvidence()
    {
        _ai.SetupFindings(
            Finding(evidence: [$"fact:{Guid.NewGuid()}"]),     // unknown -> dropped
            Finding(evidence: [ArtifactRef]));                 // valid -> kept

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Value!.Findings, Has.Count.EqualTo(1));
        Assert.That(_assessmentRepo.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RunAssessment_StripsUnresolvableEvidenceButKeepsGroundedFinding()
    {
        _ai.SetupFindings(Finding(evidence: [FactRef, $"rel:{Guid.NewGuid()}"]));

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Value!.Findings, Has.Count.EqualTo(1));
        Assert.That(result.Value.Findings[0].Evidence, Is.EqualTo(new[] { FactRef }));
    }

    [Test]
    public async Task RunAssessment_CapsAcceptedFindingsAt20()
    {
        var many = Enumerable.Range(0, 25).Select(_ => Finding(severity: "Low", evidence: [FactRef])).ToArray();
        _ai.SetupFindings(many);

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Value!.Findings, Has.Count.EqualTo(ContinuityAuditService.MaxFindings));
        Assert.That(_assessmentRepo.Findings, Has.Count.EqualTo(20));
    }

    [Test]
    public async Task RunAssessment_RecordsUsageOnSuccess()
    {
        _ai.SetupFindings(Finding(evidence: [ArtifactRef]));

        await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));
        var record = _usageRepo.Records[0];
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.ContinuityAudit));
        Assert.That(record.Succeeded, Is.True);
        Assert.That(record.WorldId, Is.EqualTo(_worldId));
        Assert.That(record.InputTokens, Is.GreaterThan(0));
        Assert.That(record.EstimatedCostUsd, Is.GreaterThan(0));
    }

    [Test]
    public async Task RunAssessment_RecordsUsageOnFailure_AndReturns503()
    {
        _ai.SetupFailure(new HttpRequestException("boom"));

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));
        Assert.That(_usageRepo.Records[0].Succeeded, Is.False);
        Assert.That(_usageRepo.Records[0].ErrorCode, Is.EqualTo("ServiceError"));
        Assert.That(_assessmentRepo.Assessments, Is.Empty);
    }

    [Test]
    public async Task DismissFinding_TransitionsOpenToDismissed_AndRaisesEffectiveScore()
    {
        _ai.SetupFindings(Finding(severity: "High", evidence: [ArtifactRef]));
        var run = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);
        var findingId = run.Value!.Findings[0].Id;
        var effectiveBefore = run.Value.EffectiveScore;

        var dismissed = await _service.DismissFindingAsync(_worldId, findingId, CancellationToken.None);

        Assert.That(dismissed.IsSuccess, Is.True);
        Assert.That(dismissed.Value!.Status, Is.EqualTo(ContinuityFindingStatus.Dismissed.ToString()));

        var latest = await _service.GetLatestAsync(_worldId, CancellationToken.None);
        // The High finding penalised the score by 12; dismissing it restores those points.
        Assert.That(latest.Value!.EffectiveScore, Is.EqualTo(effectiveBefore + 12));
    }

    [Test]
    public async Task DismissFinding_UnknownId_Returns404()
    {
        var result = await _service.DismissFindingAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task DismissFinding_WrongWorld_Returns404()
    {
        _ai.SetupFindings(Finding(evidence: [ArtifactRef]));
        var run = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);
        var findingId = run.Value!.Findings[0].Id;

        var result = await _service.DismissFindingAsync(Guid.NewGuid(), findingId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_assessmentRepo.Findings[0].Status, Is.EqualTo(ContinuityFindingStatus.Open));
    }

    [Test]
    public async Task RunAssessment_ResolvesEvidenceItemsWithLabelsAndNavigation()
    {
        _ai.SetupFindings(Finding(evidence: [FactRef, ArtifactRef]));

        var result = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        var finding = result.Value!.Findings[0];
        Assert.That(finding.IsStale, Is.False);
        Assert.That(finding.EvidenceItems, Has.Count.EqualTo(2));

        var factItem = finding.EvidenceItems[0];
        Assert.That(factItem.Kind, Is.EqualTo("Fact"));
        Assert.That(factItem.Label, Is.EqualTo("Captain Voss — location: Black Harbor"));
        Assert.That(factItem.ArtifactId, Is.EqualTo(_voss.Id));
        Assert.That(factItem.ChangedSinceAudit, Is.False);
        Assert.That(factItem.Missing, Is.False);

        var artifactItem = finding.EvidenceItems[1];
        Assert.That(artifactItem.Kind, Is.EqualTo("Artifact"));
        Assert.That(artifactItem.Label, Is.EqualTo("Captain Voss"));
        Assert.That(artifactItem.ArtifactId, Is.EqualTo(_voss.Id));
    }

    [Test]
    public async Task GetLatest_CitedFactEditedAfterAudit_MarksFindingStaleAndSuspendsPenalty()
    {
        _ai.SetupFindings(Finding(severity: "High", evidence: [FactRef]));
        var run = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);
        var effectiveBefore = run.Value!.EffectiveScore;

        _vossFact.Value = "Aboard the Grey Gull";
        _vossFact.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var latest = await _service.GetLatestAsync(_worldId, CancellationToken.None);

        var finding = latest.Value!.Findings[0];
        Assert.That(finding.Status, Is.EqualTo(ContinuityFindingStatus.Open.ToString()));
        Assert.That(finding.IsStale, Is.True);
        Assert.That(finding.EvidenceItems[0].ChangedSinceAudit, Is.True);
        // The stale High finding's 12-point penalty is suspended until a re-run verifies it.
        Assert.That(latest.Value.EffectiveScore, Is.EqualTo(effectiveBefore + 12));
    }

    [Test]
    public async Task GetLatest_CitedFactDeletedAfterAudit_MarksEvidenceMissingAndFindingStale()
    {
        _ai.SetupFindings(Finding(evidence: [FactRef]));
        await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        await _factRepo.DeleteAsync(_vossFact.Id);

        var latest = await _service.GetLatestAsync(_worldId, CancellationToken.None);

        var finding = latest.Value!.Findings[0];
        Assert.That(finding.IsStale, Is.True);
        Assert.That(finding.EvidenceItems[0].Missing, Is.True);
        Assert.That(finding.EvidenceItems[0].ArtifactId, Is.Null);
        Assert.That(finding.EvidenceItems[0].Label, Is.EqualTo("No longer in the record"));
    }

    [Test]
    public async Task GetLatest_UntouchedEvidence_StaysCountedAndUnchanged()
    {
        _ai.SetupFindings(Finding(severity: "High", evidence: [FactRef]));
        var run = await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        var latest = await _service.GetLatestAsync(_worldId, CancellationToken.None);

        Assert.That(latest.Value!.Findings[0].IsStale, Is.False);
        Assert.That(latest.Value.EffectiveScore, Is.EqualTo(run.Value!.EffectiveScore));
    }

    [Test]
    public async Task GetLatest_RelationshipEvidence_ResolvesEndpointNames()
    {
        var guild = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Faction,
            Name = "Harbor Guild",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(guild);
        var rel = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = _voss.Id,
            ArtifactBId = guild.Id,
            Type = "MemberOf",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _relationshipRepo.Seed(rel);

        _ai.SetupFindings(Finding(evidence: [$"rel:{rel.Id}"]));
        await _service.RunAssessmentAsync(_worldId, Guid.NewGuid(), CancellationToken.None);

        var latest = await _service.GetLatestAsync(_worldId, CancellationToken.None);

        var item = latest.Value!.Findings[0].EvidenceItems[0];
        Assert.That(item.Kind, Is.EqualTo("Relationship"));
        Assert.That(item.Label, Is.EqualTo("Captain Voss ↔ Harbor Guild — MemberOf"));
        Assert.That(item.ArtifactId, Is.EqualTo(_voss.Id));
    }

    [Test]
    public async Task GetLatest_NoAssessment_ReturnsHasDataFalse()
    {
        var result = await _service.GetLatestAsync(_worldId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.HasData, Is.False);
        Assert.That(result.Value.Findings, Is.Empty);
    }

    [Test]
    public async Task RunAssessment_ArchivedArtifactsStayOutOfThePrompt()
    {
        _artifactRepo.Seed(new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Merged Leftover Voss",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Archived,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        _ai.SetupFindings();

        await _service.RunAssessmentAsync(_worldId, null, CancellationToken.None);

        Assert.That(_ai.LastRequest!.UserMessage, Does.Contain("Captain Voss"));
        Assert.That(_ai.LastRequest.UserMessage, Does.Not.Contain("Merged Leftover Voss"));
    }

    [Test]
    public void FormatWorldRecord_CapsQuotesAndNamesTheOmission()
    {
        var refs = Enumerable.Range(0, ContinuityAuditService.MaxQuotesInAudit + 15)
            .Select(i => new SourceReference
            {
                Id = Guid.NewGuid(),
                SourceId = Guid.NewGuid(),
                TargetType = SourceReferenceTargetType.Artifact,
                TargetId = Guid.NewGuid(),
                Quote = $"Quote number {i}",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            })
            .ToList();

        var record = ContinuityAuditService.FormatWorldRecord([], [], [], refs, []);

        Assert.That(record, Does.Contain("Quote number 0"));
        Assert.That(record, Does.Not.Contain($"Quote number {ContinuityAuditService.MaxQuotesInAudit + 5}"));
        Assert.That(record, Does.Contain("(+15 older quotes omitted)"));
    }
}
