using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Narration decorates a ranking the system already decided. Two things therefore matter more
/// than what the model says: that every way this can fail still returns the ranking, and that a
/// success never changes the order. The prompt is asserted on what it carries, per the
/// leak-surface pattern — the model's reply is not the subject.
/// </summary>
[TestFixture]
public class ConvergenceNarrationServiceTests
{
    private StubGauge _gauge = null!;
    private CapturingNarrationClient _client = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private ConvergenceNarrationService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _gauge = new StubGauge();
        _client = new CapturingNarrationClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _usageRepo = new InMemoryAiUsageRecordRepository();

        _sut = new ConvergenceNarrationService(
            _gauge, _client, _budgetGuard, TestUsageRecorder.Wrap(_usageRepo),
            Options.Create(new LoremasterOptions { AiModel = "gpt-4o", AiTimeoutSeconds = 30 }));
    }

    private Task<AppResult<ConvergenceGauge>> Narrate() =>
        _sut.NarrateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

    #region The ranking survives every failure

    [Test]
    public async Task OverBudget_ReturnsTheRankingUnannotated()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80), Candidate("Tavrin", 40)];
        _budgetGuard.Exceeded = true;

        var result = await Narrate();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, "the GM asked for the page; the sentence was optional");
            Assert.That(result.Value!.Candidates, Has.Count.EqualTo(2));
            Assert.That(result.Value.Candidates.All(c => c.Rationale is null), Is.True);
            Assert.That(_client.Calls, Is.Zero, "nothing should be bought once the budget is spent");
        });
    }

    [Test]
    public async Task WhenTheCallFails_ReturnsTheRankingAndRecordsTheSpend()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80)];
        _client.Throw = true;

        var result = await Narrate();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Candidates.Single().Rationale, Is.Null);
            Assert.That(_usageRepo.Records, Has.Count.EqualTo(1),
                "a failed call still spent something, and only recorded spend reaches the guard");
            Assert.That(_usageRepo.Records[0].Succeeded, Is.False);
        });
    }

    [Test]
    public async Task WhenNoAiIsConfigured_ReturnsTheRankingAndMetersNothing()
    {
        // The no-op client stands in for an unconfigured host. Narration degrades rather than
        // throwing, so the gauge keeps working where the other AI features cannot.
        var service = new ConvergenceNarrationService(
            _gauge, new NoOpConvergenceNarrationClient(), _budgetGuard,
            TestUsageRecorder.Wrap(_usageRepo),
            Options.Create(new LoremasterOptions { AiModel = "gpt-4o", AiTimeoutSeconds = 30 }));
        _gauge.Candidates = [Candidate("Captain Voss", 80)];

        var result = await service.NarrateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Candidates.Single().Rationale, Is.Null);
            Assert.That(_usageRepo.Records, Is.Empty,
                "a call that never happened must not leave a row in the ledger");
        });
    }

    [Test]
    public async Task ANonGm_IsRefusedByTheGaugesOwnGate()
    {
        _gauge.Error = new AppError(403, "insufficient_role", "Only GMs can read the convergence gauge.");

        var result = await _sut.NarrateAsync(WorldId, GmId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(_client.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task AnEmptyGauge_BuysNothing()
    {
        _gauge.Candidates = [];

        var result = await Narrate();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_client.Calls, Is.Zero);
        });
    }

    #endregion

    #region It annotates, it does not rank

    [Test]
    public async Task Narration_LeavesTheOrderAndTheScoresExactlyAsTheyWere()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80), Candidate("Tavrin", 40), Candidate("Kelda", 10)];
        var before = _gauge.Candidates.Select(c => (c.Id, c.Score)).ToList();

        // Only the middle one, and named out of order. Annotating all three would make this
        // pass against a service that sorted by "has a rationale" — every key equal, stable
        // sort, no movement. One annotated row in the middle is what makes the assertion bite.
        _client.Reply = ids => [new ConvergenceNarration(ids[1], "a moment")];

        var result = await Narrate();
        var after = result.Value!.Candidates.Select(c => (c.Id, c.Score)).ToList();

        Assert.That(after, Is.EqualTo(before).AsCollection,
            "the model annotates a ranking; it never produces one");
    }

    [Test]
    public async Task Narration_AttachesEachSentenceToItsOwnCandidate()
    {
        var voss = Candidate("Captain Voss", 80);
        var tavrin = Candidate("Tavrin", 40);
        _gauge.Candidates = [voss, tavrin];

        _client.Reply = _ => [new ConvergenceNarration(tavrin.Id, "Tavrin's moment")];

        var result = await Narrate();

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Candidates.Single(c => c.Id == tavrin.Id).Rationale, Is.EqualTo("Tavrin's moment"));
            Assert.That(result.Value.Candidates.Single(c => c.Id == voss.Id).Rationale, Is.Null,
                "a candidate the model said nothing about keeps saying nothing");
        });
    }

    [Test]
    public async Task ASentenceForAnUnknownId_IsDropped()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80)];
        _client.Reply = _ => [new ConvergenceNarration(Guid.NewGuid(), "about nothing here")];

        var result = await Narrate();

        Assert.That(result.Value!.Candidates.Single().Rationale, Is.Null);
    }

    [Test]
    public async Task OnlyTheTopCandidatesAreBoughtFor()
    {
        _gauge.Candidates = Enumerable.Range(0, ConvergenceNarrationService.MaxNarrated + 5)
            .Select(i => Candidate($"Entry {i}", 90 - i))
            .ToList();

        await Narrate();

        // The gauge shows fifty; paying for a sentence on every one buys rows nobody scrolls to.
        Assert.That(_client.LastPromptIds, Has.Count.EqualTo(ConvergenceNarrationService.MaxNarrated));
    }

    #endregion

    #region The prompt

    [Test]
    public async Task ThePrompt_CarriesTheObservationsTheScoreWasBuiltFrom()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80, daysHidden: 94, contradiction: ContinuityFindingSeverity.High)];

        await Narrate();

        Assert.Multiple(() =>
        {
            Assert.That(_client.LastUserMessage, Does.Contain("Captain Voss"));
            Assert.That(_client.LastUserMessage, Does.Contain("94"));
            Assert.That(_client.LastUserMessage, Does.Contain("contradicts"));
        });
    }

    [Test]
    public async Task ThePrompt_ForbidsReRanking()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80)];

        await Narrate();

        // The instruction is the only thing standing between this and a page whose numbers and
        // sentences argue with each other.
        Assert.That(_client.LastSystemPrompt, Does.Contain("order is settled"));
    }

    [Test]
    public async Task ThePrompt_ForbidsSuggestingAReveal()
    {
        _gauge.Candidates = [Candidate("Captain Voss", 80)];

        await Narrate();

        Assert.That(_client.LastSystemPrompt, Does.Contain("Never suggest revealing"),
            "the GM decides; the review gate is not the only place that has to respect that");
    }

    #endregion

    #region Doubles

    private static ConvergenceCandidate Candidate(
        string anchorName,
        int score,
        int daysHidden = 30,
        ContinuityFindingSeverity? contradiction = null) => new()
        {
            Kind = ConvergenceCandidateKind.Fact,
            Id = Guid.NewGuid(),
            AnchorArtifactId = Guid.NewGuid(),
            AnchorName = anchorName,
            Description = "true allegiance: sworn to the cult",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysHidden),
            MissingArtifactIds = [],
            Components = ConvergenceScore.Components(
                daysHidden, 3, 0, null, contradiction, contradictionAssessed: true),
            Score = score
        };

    private sealed class StubGauge : IConvergenceGaugeService
    {
        public IReadOnlyList<ConvergenceCandidate> Candidates { get; set; } = [];
        public AppError? Error { get; set; }

        public Task<AppResult<ConvergenceGauge>> GetGaugeAsync(
            Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct) =>
            Task.FromResult(Error is not null
                ? AppResult<ConvergenceGauge>.Fail(Error)
                : AppResult<ConvergenceGauge>.Success(new ConvergenceGauge
                {
                    WorldId = worldId,
                    GeneratedAt = DateTimeOffset.UtcNow,
                    AssessmentId = null,
                    TotalCandidates = Candidates.Count,
                    Candidates = Candidates
                }));
    }

    private sealed class CapturingNarrationClient : IConvergenceNarrationClient
    {
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public string LastSystemPrompt { get; private set; } = string.Empty;
        public string LastUserMessage { get; private set; } = string.Empty;
        public List<Guid> LastPromptIds { get; } = [];
        public Func<IReadOnlyList<Guid>, IReadOnlyList<ConvergenceNarration>>? Reply { get; set; }

        public Task<ConvergenceNarrationAiResponse> NarrateAsync(AiPromptRequest request, CancellationToken ct)
        {
            Calls++;
            LastSystemPrompt = request.SystemPrompt;
            LastUserMessage = request.UserMessage;

            LastPromptIds.Clear();
            foreach (var line in request.UserMessage.Split('\n'))
            {
                if (line.StartsWith("id: ", StringComparison.Ordinal)
                    && Guid.TryParse(line["id: ".Length..].Trim(), out var id))
                {
                    LastPromptIds.Add(id);
                }
            }

            if (Throw)
            {
                throw new InvalidOperationException("the model was unreachable");
            }

            return Task.FromResult(new ConvergenceNarrationAiResponse
            {
                Narrations = Reply?.Invoke(LastPromptIds) ?? [],
                Usage = new AiUsage
                {
                    Model = "gpt-4o",
                    InputTokens = 100,
                    OutputTokens = 20,
                    TotalTokens = 120,
                    DurationMs = 250
                }
            });
        }
    }

    #endregion
}
