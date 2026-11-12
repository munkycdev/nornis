using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The check-in service (feature 44) over in-memory repositories: who may, when it is due, what
/// a start persists, what each answer does and through which provenance path, and the brief's
/// cap. The candidate rules themselves are <see cref="CheckInCandidatesTests"/>.
/// </summary>
[TestFixture]
public class CheckInServiceTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private InMemoryWorldRepository _worldRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryCheckInRepository _checkInRepo = null!;
    private InMemoryEditorialRuleRepository _ruleRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private FakeCheckInAiClient _aiClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private FakeProposalApplicator _applicator = null!;

    private World _world = null!;
    private Guid _gmUserId;

    [SetUp]
    public void SetUp()
    {
        _worldRepo = new InMemoryWorldRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _checkInRepo = new InMemoryCheckInRepository();
        _ruleRepo = new InMemoryEditorialRuleRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeCheckInAiClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _applicator = new FakeProposalApplicator();

        _gmUserId = Guid.NewGuid();
        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Vespergale Reach",
            CreatedByUserId = _gmUserId,
            CreatedAt = Base,
            UpdatedAt = Base,
        };
        _worldRepo.CreateAsync(_world).GetAwaiter().GetResult();
    }

    private CheckInService Service(int questionCount = 5, int cadence = 3, int maxRules = 40, int remembered = 2)
    {
        _world.CheckInQuestionCount = questionCount;
        var reader = new StorylineDevelopmentReader(_artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo);
        var batchWriter = new SyntheticBatchWriter(
            _sourceRepo, _batchRepo, _proposalRepo, _sourceRefRepo, _applicator,
            new FakeArtifactSummaryRefreshQueue(), new FakeUnitOfWork());
        return new CheckInService(
            reader,
            Options.Create(new ContinuityOptions { StaleThresholdSessions = 2 }),
            Options.Create(new CheckInOptions { CadenceSessions = cadence, MaxRules = maxRules, RecentCheckInsRemembered = remembered }),
            Options.Create(new LoremasterOptions { AiModel = "gpt-4o" }),
            _worldRepo, _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo,
            _checkInRepo, _ruleRepo, _aiClient, _budgetGuard,
            TestUsageRecorder.Wrap(_usageRepo),
            batchWriter,
            new ArtifactMergeService(_artifactRepo, batchWriter, NullLogger<ArtifactMergeService>.Instance),
            NullLogger<CheckInService>.Instance);
    }

    // ---------------------------------------------------------------------- Seeds --

    private Source SeedSession(int day, string? title = null, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Type = SourceType.SessionNote,
            Title = title ?? $"Session {day}",
            Body = "…",
            Visibility = visibility,
            OccurredAt = Base.AddDays(day),
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = Base.AddDays(day),
            CreatedByUserId = _gmUserId,
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private Artifact SeedArtifact(string name, ArtifactType type, ArtifactStatus status = ArtifactStatus.Active, string? summary = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Type = type,
            Name = name,
            Summary = summary,
            Status = status,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = Base,
            UpdatedAt = Base,
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private void SeedDevelopment(Artifact storyline, Source session)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = storyline.Id,
            Predicate = "development",
            Value = $"beat at {session.Title}",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = session.OccurredAt!.Value,
            UpdatedAt = session.OccurredAt!.Value,
        };
        _factRepo.Seed(fact);
        SeedReference(session, SourceReferenceTargetType.ArtifactFact, fact.Id);
    }

    private void SeedReference(Source source, SourceReferenceTargetType targetType, Guid targetId) =>
        _sourceRefRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = source.OccurredAt ?? Base,
            Source = source,
        });

    private void SeedRelationship(Artifact a, Artifact b) =>
        _relationshipRepo.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            ArtifactAId = a.Id,
            ArtifactBId = b.Id,
            Type = "Involves",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = Base,
            UpdatedAt = Base,
        });

    /// <summary>A quiet, one-source storyline linked to a character, an old one-source item, four sessions.</summary>
    private (Artifact Warning, Artifact Koves, Artifact Lamp) SeedTypicalWorld()
    {
        var s1 = SeedSession(1, "The witch's warning");
        SeedSession(2);
        SeedSession(3);
        SeedSession(4);
        var warning = SeedArtifact("The Warning", ArtifactType.Storyline, summary: "A witch warned Koves.");
        SeedDevelopment(warning, s1);
        var koves = SeedArtifact("Koves", ArtifactType.Character);
        SeedRelationship(warning, koves);
        var lamp = SeedArtifact("Old Lamp", ArtifactType.Item);
        SeedReference(s1, SourceReferenceTargetType.Artifact, lamp.Id);
        return (warning, koves, lamp);
    }

    private static CheckInPick Pick(string key, string? rule = null, Guid? recordOn = null) => new()
    {
        CandidateKey = key,
        Question = $"Phrased {key}",
        Why = $"Because {key}",
        SuggestedRule = rule,
        RecordOnArtifactId = recordOn?.ToString(),
    };

    private AnswerCheckInQuestionCommand Answer(Guid questionId, CheckInAnswer answer, string? rule = null, Guid? recordOn = null, WorldRole role = WorldRole.GM) =>
        new(_world.Id, questionId, answer, rule, recordOn, _gmUserId, role);

    // ----------------------------------------------------------------------- Read --

    [Test]
    public async Task Get_NonGm_Is403()
    {
        var result = await Service().GetAsync(_world.Id, _gmUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Get_BeforeAnyCheckIn_ComesDueAfterCadenceDatedSessions()
    {
        SeedSession(1);
        SeedSession(2);
        var sut = Service(cadence: 3);

        var notYet = await sut.GetAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        SeedSession(3);
        var due = await sut.GetAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notYet.Value!.Due, Is.False);
            Assert.That(notYet.Value.SessionsSinceLast, Is.EqualTo(2));
            Assert.That(notYet.Value.LastCheckInAt, Is.Null);
            Assert.That(notYet.Value.Current, Is.Null);
            Assert.That(due.Value!.Due, Is.True);
            Assert.That(due.Value.CadenceSessions, Is.EqualTo(3));
            Assert.That(due.Value.QuestionCount, Is.EqualTo(5));
        });
    }

    // ---------------------------------------------------------------------- Start --

    [Test]
    public async Task Start_BudgetExceeded_OpensNothing_AndCallsNoAi()
    {
        SeedTypicalWorld();
        _budgetGuard.Exceeded = true;

        var result = await Service().StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("ai_budget_exceeded"));
        Assert.That(_aiClient.Requests, Is.Empty);
        Assert.That(_checkInRepo.CheckIns, Is.Empty);
    }

    [Test]
    public async Task Start_PersistsThePicksAsAsked_TheRestAsUnasked_AndRecordsUsage()
    {
        var (warning, koves, lamp) = SeedTypicalWorld();
        _aiClient.SetupPicks(Pick("c1", rule: "Koves's curse is background.", recordOn: koves.Id));

        var result = await Service().StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var round = result.Value!.Current!;
        var asked = round.Questions.Where(q => q.Asked).ToList();
        var unasked = round.Questions.Where(q => !q.Asked).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(asked, Has.Count.EqualTo(1));
            Assert.That(asked[0].SubjectArtifactId, Is.EqualTo(warning.Id));
            Assert.That(asked[0].Text, Is.EqualTo("Phrased c1"));
            Assert.That(asked[0].Why, Is.EqualTo("Because c1"));
            Assert.That(asked[0].SuggestedRule, Is.EqualTo("Koves's curse is background."));
            Assert.That(asked[0].SuggestedRecordOnArtifactId, Is.EqualTo(koves.Id));
            Assert.That(asked[0].AllowedAnswers, Is.EqualTo(CheckInAnswers.AllowedFor(CheckInQuestionKind.Storyline)));
            Assert.That(asked[0].Evidence.RelatedEntries.Select(e => e.Id), Does.Contain(koves.Id));
            Assert.That(unasked.Select(q => q.SubjectArtifactId), Is.EqualTo(new Guid?[] { lamp.Id }));
            Assert.That(unasked[0].Text, Is.EqualTo("Keep “Old Lamp” (Item) in the record?"), "unpicked keep the templated question");
            Assert.That(round.Questions.Select(q => q.Order), Is.EqualTo([0, 1]));
            Assert.That(result.Value.Due, Is.False, "the new check-in is the anchor");
            Assert.That(result.Value.LastCheckInAt, Is.Not.Null);
        });

        var stored = _checkInRepo.CheckIns.Single();
        Assert.That(stored.Model, Is.EqualTo("gpt-4o"));
        Assert.That(stored.LatestSessionAt, Is.EqualTo(Base.AddDays(4)));
        var usage = _usageRepo.Records.Single();
        Assert.That(usage.OperationType, Is.EqualTo(AiOperationType.CheckIn));
        Assert.That(usage.WorldId, Is.EqualTo(_world.Id));

        var request = _aiClient.Requests.Single();
        Assert.That(request.UserMessage, Does.Contain("[c1]").And.Contain("[c2]"));
        Assert.That(request.Model, Is.EqualTo("gpt-4o"));
    }

    [Test]
    public async Task Start_DropsUnknownKeysDuplicatesForeignRecordOns_AndHoldsToTheWorldsCount()
    {
        var (warning, _, lamp) = SeedTypicalWorld();
        _aiClient.SetupPicks(
            Pick("zzz"),
            Pick("c2"),
            Pick("c2"),
            Pick("c1", recordOn: Guid.NewGuid()));

        var result = await Service(questionCount: 1).StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        var asked = result.Value!.Current!.Questions.Where(q => q.Asked).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(asked, Has.Count.EqualTo(1), "one question per the world's count");
            Assert.That(asked[0].SubjectArtifactId, Is.EqualTo(lamp.Id), "the model's first valid pick");
            Assert.That(result.Value.Current.Questions.Count(q => !q.Asked && q.SubjectArtifactId == warning.Id), Is.EqualTo(1));
        });

        var resolved = CheckInService.ResolvePicks(
            [Pick("c1", recordOn: Guid.NewGuid())],
            [new CheckInCandidate("c1", CheckInQuestionKind.Storyline, warning.Id, "x", "t", "w", new CheckInEvidenceBlock())],
            5);
        Assert.That(resolved.Single().Pick.RecordOnArtifactId, Is.Null, "a record-on outside the related entries is dropped");
    }

    [Test]
    public async Task Start_ClosesThePreviousOpenCheckIn_WhoseQuestionsCanNoLongerBeAnswered()
    {
        SeedTypicalWorld();
        _aiClient.SetupPicks(Pick("c1"));
        var sut = Service();

        var first = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        var firstQuestion = first.Value!.Current!.Questions[0].Id;
        var second = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        var stale = await sut.AnswerAsync(Answer(firstQuestion, CheckInAnswer.Pursuing), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_checkInRepo.CheckIns.Single(c => c.Id == first.Value.Current.Id).ClosedAt, Is.Not.Null);
            Assert.That(_checkInRepo.CheckIns.Single(c => c.Id == second.Value!.Current!.Id).ClosedAt, Is.Null);
            Assert.That(stale.Error!.Code, Is.EqualTo("check_in_closed"));
        });
    }

    [Test]
    public async Task Start_WithNothingToDoubt_MakesNoAiCall_AndOpensAnEmptyRound()
    {
        SeedSession(1);
        SeedSession(2);
        SeedSession(3);

        var result = await Service().StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Current!.Questions, Is.Empty);
        Assert.That(_aiClient.Requests, Is.Empty);
        Assert.That(_usageRepo.Records, Is.Empty);
    }

    [Test]
    public async Task Start_AiFailure_LeavesThePreviousCheckInOpen_AndRecordsTheFailure()
    {
        SeedTypicalWorld();
        var sut = Service();
        var first = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        _aiClient.SetupFailure(new InvalidOperationException("boom"));

        var failed = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error!.Code, Is.EqualTo("ai_call_failed"));
            Assert.That(failed.Error.StatusCode, Is.EqualTo(502));
            Assert.That(_checkInRepo.CheckIns, Has.Count.EqualTo(1));
            Assert.That(_checkInRepo.CheckIns.Single().Id, Is.EqualTo(first.Value!.Current!.Id));
            Assert.That(_checkInRepo.CheckIns.Single().ClosedAt, Is.Null);
            Assert.That(_usageRepo.Records.Count(r => !r.Succeeded), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Start_DoesNotAskAgainAboutASubjectAnsweredLastTime()
    {
        var (warning, _, _) = SeedTypicalWorld();
        _aiClient.SetupPicks(Pick("c1"));
        var sut = Service();
        var first = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        var question = first.Value!.Current!.Questions.Single(q => q.SubjectArtifactId == warning.Id);
        await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Pursuing), CancellationToken.None);

        var second = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(second.Value!.Current!.Questions.Select(q => q.SubjectArtifactId), Does.Not.Contain(warning.Id));
    }

    // --------------------------------------------------------------------- Answers --

    private async Task<(CheckInService Sut, CheckInQuestionView Question)> StartAndTake(
        Func<CheckInQuestionView, bool> which, CheckInPick[]? picks = null, int maxRules = 40)
    {
        var sut = Service(maxRules: maxRules);
        _aiClient.SetupPicks(picks ?? [Pick("c1")]);
        var started = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        Assert.That(started.IsSuccess, Is.True, started.Error?.Message);
        return (sut, started.Value!.Current!.Questions.Single(which));
    }

    [Test]
    public async Task Answer_Done_SetsResolved_ThroughACheckInBatch_AndStampsTheQuestion()
    {
        var (warning, _, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Done), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var batch = _batchRepo.Batches.Single(b => b.Kind == ReviewBatchKinds.CheckIn);
        var proposal = _proposalRepo.Proposals.Single(p => p.ReviewBatchId == batch.Id);
        var stored = _checkInRepo.CheckIns.Single().Questions.Single(q => q.Id == question.Id);
        Assert.Multiple(() =>
        {
            Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.UpdateArtifact));
            Assert.That(proposal.TargetId, Is.EqualTo(warning.Id));
            Assert.That(proposal.ProposedValueJson, Does.Contain("Resolved"));
            Assert.That(_applicator.AppliedProposalIds, Does.Contain(proposal.Id));
            Assert.That(stored.Answer, Is.EqualTo(CheckInAnswer.Done));
            Assert.That(stored.ActionBatchId, Is.EqualTo(batch.Id));
            Assert.That(stored.AnsweredByUserId, Is.EqualTo(_gmUserId));
            Assert.That(stored.RuleId, Is.Null);
            Assert.That(result.Value!.Question.Outcome, Is.EqualTo("Set Resolved"));
            Assert.That(result.Value.Rule, Is.Null);
        });
    }

    [Test]
    public async Task Answer_BackgroundWithAnEntry_MergesTheStorylineIntoIt()
    {
        var (warning, koves, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Background, recordOn: koves.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var merge = _proposalRepo.Proposals.Single(p => p.ChangeType == ReviewChangeType.MergeArtifact);
        Assert.Multiple(() =>
        {
            Assert.That(merge.TargetId, Is.EqualTo(koves.Id));
            Assert.That(merge.ProposedValueJson, Does.Contain(warning.Id.ToString()));
            Assert.That(_batchRepo.Batches.Select(b => b.Kind), Does.Contain(ReviewBatchKinds.ArtifactMerge).And.Not.Contain(ReviewBatchKinds.CheckIn));
            Assert.That(result.Value!.Question.Outcome, Is.EqualTo("Merged into Koves"));
            Assert.That(_checkInRepo.CheckIns.Single().Questions.Single(q => q.Id == question.Id).RecordedOnArtifactId, Is.EqualTo(koves.Id));
        });
    }

    [Test]
    public async Task Answer_BackgroundWithoutAnEntry_SetsDormant()
    {
        var (warning, _, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Background), CancellationToken.None);

        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.ProposedValueJson, Does.Contain("Dormant"));
        Assert.That(result.Value!.Question.Outcome, Is.EqualTo("Set Dormant"));
    }

    [Test]
    public async Task Answer_BackgroundWithAnEntryOutsideTheWorld_Is400_AndActsOnNothing()
    {
        var (warning, _, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Background, recordOn: Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("invalid_record_on"));
        Assert.That(_proposalRepo.Proposals, Is.Empty);
        Assert.That(_checkInRepo.CheckIns.Single().Questions.Single(q => q.Id == question.Id).IsAnswered, Is.False);
    }

    [Test]
    public async Task Answer_WithAnInstruction_WritesItLinkedToTheQuestion_AndSaysSo()
    {
        var (warning, _, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Pursuing, rule: "  Keep following the witch. "), CancellationToken.None);

        var rule = _ruleRepo.Rules.Single();
        Assert.Multiple(() =>
        {
            Assert.That(rule.Text, Is.EqualTo("Keep following the witch."));
            Assert.That(rule.Origin, Is.EqualTo(EditorialRuleOrigin.CheckIn));
            Assert.That(rule.CheckInQuestionId, Is.EqualTo(question.Id));
            Assert.That(result.Value!.Question.RuleId, Is.EqualTo(rule.Id));
            Assert.That(result.Value.Question.Outcome, Is.EqualTo("Pursuing · instruction added"));
            Assert.That(result.Value.Rule!.QuestionText, Is.EqualTo(question.Text));
            Assert.That(_proposalRepo.Proposals, Is.Empty, "Pursuing changes nothing in the record");
        });
    }

    [Test]
    public async Task Answer_EntryArchive_ArchivesThroughACheckInBatch()
    {
        var (_, _, lamp) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == lamp.Id, picks: [Pick("c2")]);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Archive), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(_proposalRepo.Proposals.Single().ProposedValueJson, Does.Contain("Archived"));
        Assert.That(result.Value!.Question.Outcome, Is.EqualTo("Archived"));
    }

    [Test]
    public async Task Answer_PatternStop_WithoutText_WritesTheTemplatedInstruction()
    {
        SeedSession(1);
        SeedSession(2);
        SeedSession(3);
        var a = SeedArtifact("A", ArtifactType.Character);
        var b = SeedArtifact("B", ArtifactType.Character);
        var c = SeedArtifact("C", ArtifactType.Character);
        for (var i = 0; i < 8; i++)
        {
            _factRepo.Seed(new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = new[] { a, b, c }[i % 3].Id,
                Predicate = "eye colour",
                Value = $"shade {i}",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = Base,
                UpdatedAt = Base,
            });
        }
        var (sut, question) = await StartAndTake(q => q.Kind == CheckInQuestionKind.Pattern);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Stop), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(_ruleRepo.Rules.Single().Text, Is.EqualTo("Do not record \"eye colour\" facts; they are incidental."));
        Assert.That(result.Value!.Question.Outcome, Is.EqualTo("Stopped recording · instruction added"));
        Assert.That(_proposalRepo.Proposals, Is.Empty);

        // Stopped is settled for good: the next check-in does not ask about the predicate.
        var next = await sut.StartAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        Assert.That(next.Value!.Current!.Questions.Where(q => q.Kind == CheckInQuestionKind.Pattern), Is.Empty);
    }

    [Test]
    public async Task Answer_Skip_WritesNoInstruction_EvenWhenTextIsGiven()
    {
        var (warning, _, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Skip, rule: "ignored"), CancellationToken.None);

        Assert.That(_ruleRepo.Rules, Is.Empty);
        Assert.That(result.Value!.Question.Outcome, Is.EqualTo("Skipped"));
    }

    [Test]
    public async Task Answer_OnceOnly_InvalidForKind_AndNonGm()
    {
        var (warning, _, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);

        var wrongKind = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Archive), CancellationToken.None);
        var player = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Pursuing, role: WorldRole.Player), CancellationToken.None);
        var first = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Pursuing), CancellationToken.None);
        var again = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Done), CancellationToken.None);
        var unknown = await sut.AnswerAsync(Answer(Guid.NewGuid(), CheckInAnswer.Done), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(wrongKind.Error!.Code, Is.EqualTo("invalid_answer"));
            Assert.That(player.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(again.Error!.Code, Is.EqualTo("already_answered"));
            Assert.That(unknown.Error!.StatusCode, Is.EqualTo(404));
        });
    }

    [Test]
    public async Task Answer_WhenTheBriefIsFull_RefusesBeforeActing()
    {
        var (warning, _, _) = SeedTypicalWorld();
        _ruleRepo.Seed(new EditorialRule { Id = Guid.NewGuid(), WorldId = _world.Id, Text = "one", Origin = EditorialRuleOrigin.Manual, CreatedAt = Base, CreatedByUserId = _gmUserId });
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id, maxRules: 1);

        var result = await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Done, rule: "two"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("brief_full"));
            Assert.That(result.Error.StatusCode, Is.EqualTo(409));
            Assert.That(_proposalRepo.Proposals, Is.Empty, "nothing acted");
            Assert.That(_checkInRepo.CheckIns.Single().Questions.Single(q => q.Id == question.Id).IsAnswered, Is.False);
        });
    }

    // ----------------------------------------------------------------------- Rules --

    [Test]
    public async Task Rules_AddTrimsAndCaps_RemoveIsIdempotent_AndGetShowsThem()
    {
        var sut = Service(maxRules: 2);

        var first = await sut.AddRuleAsync(_world.Id, "  Track every ship. ", _gmUserId, WorldRole.GM, CancellationToken.None);
        var blank = await sut.AddRuleAsync(_world.Id, "   ", _gmUserId, WorldRole.GM, CancellationToken.None);
        var tooLong = await sut.AddRuleAsync(_world.Id, new string('x', EditorialRule.MaxTextChars + 1), _gmUserId, WorldRole.GM, CancellationToken.None);
        var player = await sut.AddRuleAsync(_world.Id, "no", _gmUserId, WorldRole.Player, CancellationToken.None);
        var second = await sut.AddRuleAsync(_world.Id, "Second.", _gmUserId, WorldRole.GM, CancellationToken.None);
        var third = await sut.AddRuleAsync(_world.Id, "Third.", _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.Text, Is.EqualTo("Track every ship."));
            Assert.That(first.Value.Origin, Is.EqualTo(EditorialRuleOrigin.Manual));
            Assert.That(blank.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(tooLong.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(player.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(third.Error!.Code, Is.EqualTo("brief_full"));
        });

        var view = await sut.GetAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        Assert.That(view.Value!.Rules.Select(r => r.Text), Is.EqualTo(["Track every ship.", "Second."]));
        Assert.That(view.Value.MaxRules, Is.EqualTo(2));

        var removed = await sut.RemoveRuleAsync(_world.Id, first.Value!.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        var removedAgain = await sut.RemoveRuleAsync(_world.Id, first.Value.Id, _gmUserId, WorldRole.GM, CancellationToken.None);
        Assert.That(removed.IsSuccess, Is.True);
        Assert.That(removedAgain.IsSuccess, Is.True);
        Assert.That(_ruleRepo.Rules.Select(r => r.Text), Is.EqualTo(["Second."]));
    }

    [Test]
    public async Task RemoveRule_FromAnotherWorld_Is404()
    {
        var other = new EditorialRule { Id = Guid.NewGuid(), WorldId = Guid.NewGuid(), Text = "theirs", Origin = EditorialRuleOrigin.Manual, CreatedAt = Base, CreatedByUserId = _gmUserId };
        _ruleRepo.Seed(other);

        var result = await Service().RemoveRuleAsync(_world.Id, other.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_ruleRepo.Rules, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Get_AfterAnswers_ShowsOutcomes_AndTheRuleBesideItsQuestion()
    {
        var (warning, koves, _) = SeedTypicalWorld();
        var (sut, question) = await StartAndTake(q => q.SubjectArtifactId == warning.Id);
        await sut.AnswerAsync(Answer(question.Id, CheckInAnswer.Background, rule: "Koves is background.", recordOn: koves.Id), CancellationToken.None);

        var view = await sut.GetAsync(_world.Id, _gmUserId, WorldRole.GM, CancellationToken.None);

        var shown = view.Value!.Current!.Questions.Single(q => q.Id == question.Id);
        Assert.Multiple(() =>
        {
            Assert.That(shown.Answer, Is.EqualTo(CheckInAnswer.Background));
            Assert.That(shown.Outcome, Is.EqualTo("Merged into Koves · instruction added"));
            Assert.That(view.Value.Rules.Single().QuestionText, Is.EqualTo(question.Text));
            Assert.That(view.Value.Rules.Single().CheckInQuestionId, Is.EqualTo(question.Id));
        });
    }
}
