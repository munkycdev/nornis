using Microsoft.Extensions.Logging.Abstractions;
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
public class StorylineRetrospectiveServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private FakeRetrospectiveAiClient _aiClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private StorylineRetrospectiveService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeRetrospectiveAiClient();
        _budgetGuard = new FakeAiBudgetGuard();

        var options = new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 60,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        };

        _sut = new StorylineRetrospectiveService(
            _artifactRepo,
            _factRepo,
            _sourceRepo,
            _batchRepo,
            _proposalRepo,
            _sourceRefRepo,
            _usageRepo,
            _aiClient,
            _budgetGuard,
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<StorylineRetrospectiveService>.Instance);
    }

    private Artifact SeedStoryline(string name, ArtifactStatus status = ArtifactStatus.Active)
    {
        var storyline = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Storyline,
            Name = name,
            Summary = $"The tale of {name}.",
            Visibility = VisibilityScope.PartyVisible,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        _artifactRepo.Seed(storyline);
        return storyline;
    }

    private static RetrospectiveVerdict Verdict(Guid storylineId, string verdict, string rationale = "The record shows it.") =>
        new() { StorylineId = storylineId.ToString(), Verdict = verdict, Rationale = rationale, Confidence = 0.9m };

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task RunAsync_NonGm_Returns403(WorldRole role)
    {
        var result = await _sut.RunAsync(WorldId, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_aiClient.Requests, Is.Empty);
    }

    [Test]
    public async Task RunAsync_BudgetExceeded_FailsWithoutAiCall()
    {
        SeedStoryline("Missing Caravan");
        _budgetGuard.Exceeded = true;

        var result = await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(_aiClient.Requests, Is.Empty);
    }

    [Test]
    public async Task RunAsync_NoActiveStorylines_SucceedsWithoutAiCall()
    {
        SeedStoryline("Old Arc", ArtifactStatus.Resolved);

        var result = await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AssessedCount, Is.EqualTo(0));
        Assert.That(_aiClient.Requests, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ClosureVerdicts_CreateBatchAndProposals()
    {
        var resolved = SeedStoryline("Missing Caravan");
        var dormant = SeedStoryline("Bell Tower Crisis");
        var active = SeedStoryline("Throne of Thorns");
        _aiClient.SetupVerdicts(
            Verdict(resolved.Id, "Resolved", "The caravan was found and the culprit exposed."),
            Verdict(dormant.Id, "Dormant"),
            Verdict(active.Id, "StillActive"));

        var result = await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AssessedCount, Is.EqualTo(3));
        Assert.That(result.Value.ProposedCount, Is.EqualTo(2), "StillActive produces no proposal");

        var proposals = _proposalRepo.Proposals;
        Assert.That(proposals, Has.Count.EqualTo(2));
        Assert.That(proposals.All(p => p.ChangeType == ReviewChangeType.UpdateArtifact));
        Assert.That(proposals.All(p => p.Status == ReviewProposalStatus.Pending));

        var resolvedProposal = proposals.Single(p => p.TargetId == resolved.Id);
        Assert.That(resolvedProposal.ProposedValueJson, Does.Contain("\"status\":\"Resolved\""));
        Assert.That(resolvedProposal.Rationale, Does.Contain("culprit exposed"));

        var dormantProposal = proposals.Single(p => p.TargetId == dormant.Id);
        Assert.That(dormantProposal.ProposedValueJson, Does.Contain("\"status\":\"Dormant\""));

        // Provenance: a synthetic source records the pass, and each proposal cites it.
        var source = _sourceRepo.Sources.Single();
        Assert.That(source.Title, Does.StartWith("Storyline Retrospective"));
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
        Assert.That(source.Body, Does.Contain("Missing Caravan"));
        Assert.That(_batchRepo.Batches.Single().SourceId, Is.EqualTo(source.Id));
        Assert.That(_sourceRefRepo.References.Count(r => r.TargetType == SourceReferenceTargetType.ReviewProposal), Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_AllStillActive_CreatesNothing()
    {
        var storyline = SeedStoryline("Throne of Thorns");
        _aiClient.SetupVerdicts(Verdict(storyline.Id, "StillActive"));

        var result = await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProposedCount, Is.EqualTo(0));
        Assert.That(_sourceRepo.Sources, Is.Empty, "no synthetic source when there is nothing to review");
        Assert.That(_proposalRepo.Proposals, Is.Empty);
    }

    [Test]
    public async Task RunAsync_HallucinatedStorylineId_IsIgnored()
    {
        SeedStoryline("Missing Caravan");
        _aiClient.SetupVerdicts(
            new RetrospectiveVerdict { StorylineId = Guid.NewGuid().ToString(), Verdict = "Resolved", Rationale = "?", Confidence = 0.9m },
            new RetrospectiveVerdict { StorylineId = "not-a-guid", Verdict = "Resolved", Rationale = "?", Confidence = 0.9m });

        var result = await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProposedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_AiFailure_Returns502AndTracksFailedUsage()
    {
        SeedStoryline("Missing Caravan");
        _aiClient.SetupFailure(new HttpRequestException("boom"));

        var result = await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(502));
        var usage = _usageRepo.Records.Single();
        Assert.That(usage.Succeeded, Is.False);
        Assert.That(usage.OperationType, Is.EqualTo(AiOperationType.StorylineRetrospective));
    }

    [Test]
    public async Task RunAsync_TracksSuccessfulUsageWithCost()
    {
        var storyline = SeedStoryline("Missing Caravan");
        _aiClient.SetupVerdicts(Verdict(storyline.Id, "StillActive"));

        await _sut.RunAsync(WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        var usage = _usageRepo.Records.Single();
        Assert.That(usage.Succeeded, Is.True);
        Assert.That(usage.OperationType, Is.EqualTo(AiOperationType.StorylineRetrospective));
        Assert.That(usage.EstimatedCostUsd, Is.GreaterThan(0m));
    }

    [Test]
    public void BuildUserMessage_IncludesStorylineIdsAndOpenQuestions()
    {
        var storyline = SeedStoryline("Missing Caravan");
        var facts = new List<ArtifactFact>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ArtifactId = storyline.Id,
                Predicate = "open question",
                Value = "Who hired the raiders?",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var message = StorylineRetrospectiveService.BuildUserMessage(
            [storyline],
            new Dictionary<Guid, IReadOnlyList<ArtifactFact>> { [storyline.Id] = facts });

        Assert.That(message, Does.Contain(storyline.Id.ToString()));
        Assert.That(message, Does.Contain("Who hired the raiders?"));
        Assert.That(message, Does.Contain("[OPEN]"));
    }
}
