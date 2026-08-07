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

        var batchWriter = new SyntheticBatchWriter(
            _sourceRepo, _batchRepo, _proposalRepo, _sourceRefRepo,
            new FakeProposalApplicator(), NoOpArtifactSummaryRefreshQueue.Instance, _unitOfWork);

        _service = new ContinuityFixService(
            _budgetGuard, _assessmentRepo, _artifactRepo, _factRepo, _relationshipRepo,
            batchWriter, new ProposalValidator(), _ai, TestUsageRecorder.Wrap(_usageRepo), options);

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
        ContinuityFindingStatus status = ContinuityFindingStatus.Open,
        ContinuityFindingCategory category = ContinuityFindingCategory.Contradiction)
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
            Category = category,
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
        var result = await _service.DraftFixAsync(_worldId, Guid.NewGuid(), _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_FindingFromAnotherWorld_Returns404()
    {
        var result = await _service.DraftFixAsync(Guid.NewGuid(), _finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_DismissedFinding_Returns409()
    {
        var dismissed = SeedFinding(_worldId, [$"fact:{_harborFact.Id}"], ContinuityFindingStatus.Dismissed);

        var result = await _service.DraftFixAsync(_worldId, dismissed.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_BudgetExceeded_Returns429WithoutCallingAi()
    {
        _budgetGuard.Exceeded = true;

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(429));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_AllEvidenceDeleted_Returns409()
    {
        var orphaned = SeedFinding(_worldId, [$"fact:{Guid.NewGuid()}"]);
        orphaned.ArtifactId = null;
        var result = await _service.DraftFixAsync(_worldId, orphaned.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("evidence_gone"));
        Assert.That(_ai.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DraftFix_AiFailure_Returns503AndTracksFailedUsage()
    {
        _ai.ExceptionToThrow = new HttpRequestException("boom");

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, WorldRole.GM, CancellationToken.None);

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

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var draft = result.Value!;
        Assert.That(draft.ProposalCount, Is.EqualTo(1));

        var source = _sourceRepo.Sources.Single(s => s.Id == draft.SourceId);
        Assert.That(source.Type, Is.EqualTo(SourceType.GMNote));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
        Assert.That(source.CreatedByUserId, Is.EqualTo(_userId));

        var batch = _batchRepo.Batches.Single(b => b.Id == draft.BatchId);
        Assert.That(batch.Kind, Is.EqualTo(ReviewBatchKinds.ContinuityFix));
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

        await _service.DraftFixAsync(_worldId, _finding.Id, _userId, WorldRole.GM, CancellationToken.None);

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

        var result = await _service.DraftFixAsync(_worldId, _finding.Id, _userId, WorldRole.GM, CancellationToken.None);

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

        Assert.That(drafts.Select(d => d.ChangeType), Is.EquivalentTo(
        [
            ReviewChangeType.UpdateFact,
            ReviewChangeType.UpdateArtifact,
            ReviewChangeType.UpdateRelationship,
            ReviewChangeType.AddFact
        ]));
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

    // -------------------------------------------------------------- Duplicate merge branch --

    /// <summary>
    /// One member of a duplicate pair: an active artifact whose merge weight is set by
    /// <paramref name="factCount"/> and whose age is set by <paramref name="createdAt"/> —
    /// the two axes the survivor rule ranks on.
    /// </summary>
    private Artifact SeedPairArtifact(
        string name,
        ArtifactType type = ArtifactType.Character,
        DateTimeOffset? createdAt = null,
        int factCount = 0)
    {
        var created = createdAt ?? DateTimeOffset.UtcNow.AddDays(-2);
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = created,
            UpdatedAt = created
        };
        _artifactRepo.Seed(artifact);
        for (var i = 0; i < factCount; i++)
        {
            _factRepo.Seed(Fact(artifact.Id, $"detail-{i}", $"value-{i}"));
        }

        return artifact;
    }

    private ContinuityFinding SeedDuplicateFinding(Artifact a, Artifact b) =>
        SeedFinding(_worldId, [$"artifact:{a.Id}", $"artifact:{b.Id}"],
            category: ContinuityFindingCategory.DuplicateArtifact);

    [Test]
    public async Task DraftFix_Twice_RejectsTheSecondAndMintsOneBatch()
    {
        var a = SeedPairArtifact("Karvosti", factCount: 2);
        var b = SeedPairArtifact("Karvosthi");
        var finding = SeedDuplicateFinding(a, b);

        var first = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);
        Assert.That(first.IsSuccess, Is.True);

        // A drafted fix leaves the finding Open — it is proposed, not applied — so the
        // open-status gate lets a second click straight through to a second batch of the
        // same proposals.
        var second = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(second.IsSuccess, Is.False);
        Assert.That(second.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(second.Error.Code, Is.EqualTo("fix_already_drafted"));
        Assert.That(_batchRepo.Batches.Count(x => x.Kind == ReviewBatchKinds.ContinuityFix), Is.EqualTo(1));
        Assert.That(_proposalRepo.Proposals, Has.Count.EqualTo(1),
            "the review queue should hold one merge to decide, not two identical ones");
    }

    [Test]
    public async Task DraftFix_Duplicate_DraftsTheMergeWithoutAnAiCallOrBudgetGate()
    {
        var a = SeedPairArtifact("Karvosti", factCount: 2);
        var b = SeedPairArtifact("Karvosthi");
        var finding = SeedDuplicateFinding(a, b);

        // The stronger form of "the budget guard is not consulted": an exhausted budget
        // must not block this branch, because it spends nothing.
        _budgetGuard.Exceeded = true;

        var result = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_ai.CallCount, Is.EqualTo(0), "a duplicate's merge is computed, never bought");
        Assert.That(result.Value!.ProposalCount, Is.EqualTo(1));

        var batch = _batchRepo.Batches.Single(x => x.Id == result.Value.BatchId);
        Assert.That(batch.Kind, Is.EqualTo(ReviewBatchKinds.ContinuityFix));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));

        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.MergeArtifact));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending));
    }

    [Test]
    public async Task DraftFix_Duplicate_KeepsTheRicherArtifact()
    {
        // The richer entry wins even against an older twin — weight outranks age.
        var rich = SeedPairArtifact("Karvosti", factCount: 3);
        var poor = SeedPairArtifact("Karvosthi", createdAt: DateTimeOffset.UtcNow.AddDays(-30), factCount: 1);
        var finding = SeedDuplicateFinding(rich, poor);

        var result = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.TargetId, Is.EqualTo(rich.Id));
        Assert.That(proposal.ProposedValueJson, Does.Contain($"\"sourceArtifactId\":\"{poor.Id}\""));
    }

    [Test]
    public async Task DraftFix_Duplicate_TieGoesToTheOlder()
    {
        // Equal weight: the older entry is the original, matching the create-dedup rule.
        var older = SeedPairArtifact("Karvosti", createdAt: DateTimeOffset.UtcNow.AddDays(-30), factCount: 1);
        var newer = SeedPairArtifact("Karvosthi", createdAt: DateTimeOffset.UtcNow.AddDays(-1), factCount: 1);
        var finding = SeedDuplicateFinding(newer, older);

        var result = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.TargetId, Is.EqualTo(older.Id));
        Assert.That(proposal.ProposedValueJson, Does.Contain($"\"sourceArtifactId\":\"{newer.Id}\""));
    }

    [Test]
    public async Task DraftFix_Duplicate_EvidenceGoneWhenAPairMemberIsArchivedOrMissing()
    {
        // One side archived means the pair was most likely merged already — drafting
        // against the survivor and a ghost would re-litigate a settled merge.
        var live = SeedPairArtifact("Karvosti");
        var merged = SeedPairArtifact("Karvosthi");
        merged.Status = ArtifactStatus.Archived;
        var finding = SeedDuplicateFinding(live, merged);

        var result = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("evidence_gone"));
        Assert.That(_batchRepo.Batches, Is.Empty);
        Assert.That(_sourceRepo.Sources, Is.Empty);
        Assert.That(_proposalRepo.Proposals, Is.Empty);
    }

    [Test]
    public async Task DraftFix_Duplicate_CrossTypePair_RationaleWarnsTheReviewer()
    {
        // The system permits a cross-type merge (mistyped twins are real), but the
        // reviewer must be told the pair disagrees on what kind of thing it is.
        var character = SeedPairArtifact("Karvosti", factCount: 1);
        var location = SeedPairArtifact("Karvosti", type: ArtifactType.Location);
        var finding = SeedDuplicateFinding(character, location);

        var result = await _service.DraftFixAsync(_worldId, finding.Id, _userId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_proposalRepo.Proposals.Single().Rationale, Does.Contain("typed differently"));
    }

    [Test]
    public void BuildMergeRationale_StatesDirectionAndTheReasonForIt()
    {
        var keep = SeedPairArtifact("Karvosti");
        var fold = SeedPairArtifact("Karvosthi");

        var richer = ContinuityFixService.BuildMergeRationale(keep, fold, 3, 1);
        Assert.That(richer, Does.Contain("Keeping \"Karvosti\""));
        Assert.That(richer, Does.Contain("3 facts and relationships against 1"));

        var tie = ContinuityFixService.BuildMergeRationale(keep, fold, 2, 2);
        Assert.That(tie, Does.Contain("the older entry is kept"));
    }
}
