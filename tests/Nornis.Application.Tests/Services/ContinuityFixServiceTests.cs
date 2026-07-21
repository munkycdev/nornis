using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ContinuityFixServiceTests
{
    private FakeAiBudgetGuard _budgetGuard = null!;
    private InMemoryHealthAssessmentRepository _assessmentRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private FakeContinuityFixAiClient _ai = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ContinuityFixService _service = null!;

    private Guid _worldId;
    private Guid _userId;
    private Artifact _voss = null!;
    private ArtifactFact _harborFact = null!;
    private ArtifactFact _shipFact = null!;
    private ArtifactRelationship _rel = null!;
    private ContinuityFinding _finding = null!;

    [SetUp]
    public void SetUp()
    {
        _budgetGuard = new FakeAiBudgetGuard();
        _assessmentRepo = new InMemoryHealthAssessmentRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _ai = new FakeContinuityFixAiClient();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _unitOfWork = new FakeUnitOfWork();

        var options = Options.Create(new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new() { InputPerMillionTokensUsd = 2.5m, OutputPerMillionTokensUsd = 10m }
            }
        });

        _service = new ContinuityFixService(
            _budgetGuard, _assessmentRepo, _artifactRepo, _factRepo, _relationshipRepo,
            _sourceRepo, _batchRepo, _proposalRepo, _sourceRefRepo,
            new ProposalValidator(), _ai, _usageRepo, _unitOfWork, options);

        _worldId = Guid.NewGuid();
        _userId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        _voss = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Summary = "A harbor captain.",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        _artifactRepo.Seed(_voss);

        _harborFact = Fact(_voss.Id, "location", "Black Harbor");
        _shipFact = Fact(_voss.Id, "location", "Aboard the Grey Gull");
        _factRepo.Seed(_harborFact, _shipFact);

        var other = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Faction,
            Name = "Harbor Guild",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        _artifactRepo.Seed(other);
        _rel = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            ArtifactAId = _voss.Id,
            ArtifactBId = other.Id,
            Type = "MemberOf",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = now,
            UpdatedAt = now
        };
        _relationshipRepo.Seed(_rel);

        _finding = SeedFinding(_worldId,
            [$"fact:{_harborFact.Id}", $"fact:{_shipFact.Id}", $"rel:{_rel.Id}"]);
    }

    private static ArtifactFact Fact(Guid artifactId, string predicate, string value) => new()
    {
        Id = Guid.NewGuid(),
        ArtifactId = artifactId,
        Predicate = predicate,
        Value = value,
        TruthState = TruthState.Confirmed,
        Visibility = VisibilityScope.PartyVisible,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private ContinuityFinding SeedFinding(
        Guid worldId, IReadOnlyList<string> evidence,
        ContinuityFindingStatus status = ContinuityFindingStatus.Open)
    {
        var assessment = new HealthAssessment
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CreatedAt = DateTimeOffset.UtcNow,
            Model = "gpt-4o",
            Score = 70
        };
        var finding = new ContinuityFinding
        {
            Id = Guid.NewGuid(),
            HealthAssessmentId = assessment.Id,
            Category = ContinuityFindingCategory.Contradiction,
            Severity = ContinuityFindingSeverity.High,
            Summary = "Voss is in two places at once.",
            SuggestedAction = "Reconcile the location facts.",
            EvidenceJson = System.Text.Json.JsonSerializer.Serialize(evidence),
            ArtifactId = _voss.Id,
            Status = status
        };
        _assessmentRepo.CreateAsync(assessment, [finding]).GetAwaiter().GetResult();
        return finding;
    }

    private ContinuityFixProposal RetireHarborFactProposal(string? targetRef = null) => new()
    {
        ChangeType = "UpdateFact",
        TargetRef = targetRef ?? $"[ref:fact:{_harborFact.Id}]",
        Rationale = "The record supports the ship sighting; retire the harbor location.",
        TruthState = "False",
        Confidence = 0.9m
    };

    // ------------------------------------------------------------------------- Guard rails --

    [Test]
    public async Task DraftFix_UnknownFinding_Returns404()
    {
        var result = await _service.DraftFixAsync(_worldId, Guid.NewGuid(), _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_FindingFromAnotherWorld_Returns404()
    {
        var result = await _service.DraftFixAsync(Guid.NewGuid(), _finding.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_DismissedFinding_Returns409()
    {
        var dismissed = SeedFinding(_worldId, [$"fact:{_harborFact.Id}"], ContinuityFindingStatus.Dismissed);

        var result = await _service.DraftFixAsync(_worldId, dismissed.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_BudgetExceeded_Returns429WithoutCallingAi()
    {
        _budgetGuard.Exceeded = true;

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(429));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_AllEvidenceDeleted_Returns409()
    {
        var orphaned = SeedFinding(_worldId, [$"fact:{Guid.NewGuid()}"]);
        orphaned.ArtifactId = null;
        var result = await _service.DraftFixAsync(_worldId, orphaned.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("evidence_gone"));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_AiFailure_Returns503AndTracksFailedUsage()
    {
        _ai.ExceptionToThrow = new HttpRequestException("boom");

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
        Assert.That(_usageRepo.Records, Has.Count.EqualTo(1));
        Assert.That(_usageRepo.Records[0].Succeeded, Is.False);
        Assert.That(_usageRepo.Records[0].OperationType, Is.EqualTo(AiOperationType.ContinuityFix));
    }

    // ------------------------------------------------------------------------ Happy path --

    [Test]
    public async Task DraftFix_ValidProposal_PersistsSourceBatchProposalAndReference()
    {
        _ai.Proposals = [RetireHarborFactProposal()];

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var draft = result.Value!;
        Assert.That(draft.ProposalCount, Is.EqualTo(1));

        var source = _sourceRepo.Sources.Single(s => s.Id == draft.SourceId);
        Assert.That(source.Type, Is.EqualTo(SourceType.GMNote));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
        Assert.That(source.CreatedByUserId, Is.EqualTo(_userId));

        var batch = _batchRepo.Batches.Single(b => b.Id == draft.BatchId);
        Assert.That(batch.Kind, Is.EqualTo(ContinuityFixService.BatchKind));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));
        Assert.That(batch.SourceId, Is.EqualTo(source.Id));

        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.UpdateFact));
        Assert.That(proposal.TargetType, Is.EqualTo(ReviewTargetType.ArtifactFact));
        Assert.That(proposal.TargetId, Is.EqualTo(_harborFact.Id));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending));
        Assert.That(proposal.ProposedValueJson, Does.Contain("\"truthState\":\"False\""));
        Assert.That(proposal.Confidence, Is.EqualTo(0.9m));

        var reference = _sourceRefRepo.References.Single();
        Assert.That(reference.SourceId, Is.EqualTo(source.Id));
        Assert.That(reference.TargetType, Is.EqualTo(SourceReferenceTargetType.ReviewProposal));
        Assert.That(reference.TargetId, Is.EqualTo(proposal.Id));

        Assert.That(_unitOfWork.Transactions.Single().Committed, Is.True);
        Assert.That(_usageRepo.Records.Single().Succeeded, Is.True);
    }

    [Test]
    public async Task DraftFix_PromptContainsFindingAndCitedRecord()
    {
        _ai.Proposals = [];

        await _service.DraftFixAsync(_worldId, _finding.Id, _userId, CancellationToken.None);

        Assert.That(_ai.LastRequest, Is.Not.Null);
        var message = _ai.LastRequest!.UserMessage;
        Assert.That(message, Does.Contain("Voss is in two places at once."));
        Assert.That(message, Does.Contain($"[ref:fact:{_harborFact.Id}]"));
        Assert.That(message, Does.Contain($"[ref:rel:{_rel.Id}]"));
        Assert.That(message, Does.Contain("Captain Voss"));
    }

    [Test]
    public async Task DraftFix_NoValidProposals_ReturnsZeroWithoutCreatingBatch()
    {
        _ai.Proposals = [];

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProposalCount, Is.EqualTo(0));
        Assert.That(result.Value.BatchId, Is.Null);
        Assert.That(_batchRepo.Batches, Is.Empty);
        Assert.That(_sourceRepo.Sources, Is.Empty);
        Assert.That(_usageRepo.Records.Single().Succeeded, Is.True);
    }

    // -------------------------------------------------------------------- Validation rules --

    [Test]
    public void BuildValidatedProposals_DropsUngroundedUnknownAndEmpty()
    {
        var raw = new List<ContinuityFixProposal>
        {
            RetireHarborFactProposal(),
            // Unknown change type.
            new() { ChangeType = "DeleteFact", TargetRef = $"fact:{_harborFact.Id}", Rationale = "r" },
            // Target does not resolve.
            new() { ChangeType = "UpdateFact", TargetRef = $"fact:{Guid.NewGuid()}", Rationale = "r", Value = "x" },
            // Ref kind does not match the change type.
            new() { ChangeType = "UpdateFact", TargetRef = $"artifact:{_voss.Id}", Rationale = "r", Value = "x" },
            // No effective field set.
            new() { ChangeType = "UpdateFact", TargetRef = $"fact:{_shipFact.Id}", Rationale = "r" },
            // Missing rationale.
            new() { ChangeType = "UpdateFact", TargetRef = $"fact:{_shipFact.Id}", Rationale = " ", Value = "x" },
            // AddFact without predicate.
            new() { ChangeType = "AddFact", TargetRef = $"artifact:{_voss.Id}", Rationale = "r", Value = "x" },
        };

        var drafts = ContinuityFixService.BuildValidatedProposals(
            raw, new ProposalValidator(), [_voss], [_harborFact, _shipFact], [_rel]);

        Assert.That(drafts, Has.Count.EqualTo(1));
        Assert.That(drafts[0].TargetId, Is.EqualTo(_harborFact.Id));
    }

    [Test]
    public void BuildValidatedProposals_MapsAllFourChangeTypes()
    {
        var raw = new List<ContinuityFixProposal>
        {
            new() { ChangeType = "UpdateFact", TargetRef = $"fact:{_harborFact.Id}", Rationale = "r", TruthState = "false" },
            new() { ChangeType = "UpdateArtifact", TargetRef = $"artifact:{_voss.Id}", Rationale = "r", Summary = "A captain last seen aboard the Grey Gull." },
            new() { ChangeType = "UpdateRelationship", TargetRef = $"rel:{_rel.Id}", Rationale = "r", TruthState = "Disputed" },
            new() { ChangeType = "AddFact", TargetRef = $"artifact:{_voss.Id}", Rationale = "r", Predicate = "last-seen", Value = "Grey Gull" },
        };

        var drafts = ContinuityFixService.BuildValidatedProposals(
            raw, new ProposalValidator(), [_voss], [_harborFact, _shipFact], [_rel]);

        Assert.That(drafts.Select(d => d.ChangeType), Is.EquivalentTo(new[]
        {
            ReviewChangeType.UpdateFact,
            ReviewChangeType.UpdateArtifact,
            ReviewChangeType.UpdateRelationship,
            ReviewChangeType.AddFact
        }));
        Assert.That(drafts.Single(d => d.ChangeType == ReviewChangeType.AddFact).TargetId, Is.EqualTo(_voss.Id));
        // Case-insensitive truth state parse normalizes to the canonical enum name.
        Assert.That(drafts.Single(d => d.ChangeType == ReviewChangeType.UpdateFact).ProposedValueJson,
            Does.Contain("\"truthState\":\"False\""));
    }

    [Test]
    public void BuildValidatedProposals_InvalidEnumishFieldsAreNulledNotFatal()
    {
        var raw = new List<ContinuityFixProposal>
        {
            // Bad truth state nulled; value keeps the proposal alive.
            new() { ChangeType = "UpdateFact", TargetRef = $"fact:{_harborFact.Id}", Rationale = "r", Value = "Grey Gull", TruthState = "Bogus" },
            // Bad truth state nulled and nothing else set — dropped.
            new() { ChangeType = "UpdateFact", TargetRef = $"fact:{_shipFact.Id}", Rationale = "r", TruthState = "Bogus" },
            // Out-of-range confidence nulled, proposal kept.
            new() { ChangeType = "UpdateArtifact", TargetRef = $"artifact:{_voss.Id}", Rationale = "r", Summary = "s", Confidence = 3m },
        };

        var drafts = ContinuityFixService.BuildValidatedProposals(
            raw, new ProposalValidator(), [_voss], [_harborFact, _shipFact], [_rel]);

        Assert.That(drafts, Has.Count.EqualTo(2));
        Assert.That(drafts[0].ProposedValueJson, Does.Not.Contain("truthState"));
        Assert.That(drafts[1].Confidence, Is.Null);
    }

    [Test]
    public void BuildValidatedProposals_CapsAtMaxProposals()
    {
        var raw = Enumerable.Range(0, ContinuityFixService.MaxProposals + 5)
            .Select(i => (ContinuityFixProposal)new()
            {
                ChangeType = "AddFact",
                TargetRef = $"artifact:{_voss.Id}",
                Rationale = $"r{i}",
                Predicate = $"p{i}",
                Value = "v"
            })
            .ToList();

        var drafts = ContinuityFixService.BuildValidatedProposals(
            raw, new ProposalValidator(), [_voss], [], []);

        Assert.That(drafts, Has.Count.EqualTo(ContinuityFixService.MaxProposals));
    }
}
