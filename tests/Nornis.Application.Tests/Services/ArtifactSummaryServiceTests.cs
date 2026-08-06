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
/// The accept-time summary refresh. The authorization cases are the load-bearing ones:
/// a summary is stored once per artifact and rendered to everyone who can see the page,
/// so the generation context — not just the output — must be scoped to the artifact's own
/// audience. These tests read the prompt the fake client captured, because the prompt IS
/// the leak surface.
/// </summary>
[TestFixture]
public class ArtifactSummaryServiceTests
{
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemoryWorldRepository _worldRepository = null!;
    private InMemoryReviewBatchRepository _batchRepository = null!;
    private InMemoryReviewProposalRepository _proposalRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryAiUsageRecordRepository _usageRepository = null!;
    private FakeArtifactSummaryAiClient _aiClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private ArtifactSummaryService _service = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _artifactRepository = new InMemoryArtifactRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _worldRepository = new InMemoryWorldRepository();
        _batchRepository = new InMemoryReviewBatchRepository();
        _proposalRepository = new InMemoryReviewProposalRepository();
        _sourceRepository = new InMemorySourceRepository();
        _usageRepository = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeArtifactSummaryAiClient();
        _budgetGuard = new FakeAiBudgetGuard();

        var options = Options.Create(new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            MaxFactsPerArtifact = 20
        });

        var writer = new SyntheticBatchWriter(
            _sourceRepository, _batchRepository, _proposalRepository,
            new InMemorySourceReferenceRepository(), new FakeProposalApplicator(), new FakeUnitOfWork());

        _service = new ArtifactSummaryService(
            _artifactRepository,
            _factRepository,
            _relationshipRepository,
            _worldRepository,
            _aiClient,
            _budgetGuard,
            TestUsageRecorder.Wrap(_usageRepository),
            writer,
            options,
            NullLogger<ArtifactSummaryService>.Instance);
    }

    private Task SeedWorldAsync(bool summaryReviewRequired = false) =>
        _worldRepository.CreateAsync(new World
        {
            Id = WorldId,
            Name = "Vespergale Reach",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedByUserId = GmId,
            SummaryReviewRequired = summaryReviewRequired
        });

    private Artifact SeedArtifact(
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        ArtifactStatus status = ArtifactStatus.Active,
        DateTimeOffset? summaryRefreshedAt = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Summary = "An old summary.",
            Visibility = visibility,
            Status = status,
            SummaryRefreshedAt = summaryRefreshedAt,
            CreatedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _artifactRepository.Seed(artifact);
        return artifact;
    }

    private void SeedFact(
        Guid artifactId, string predicate, string value,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        TruthState truthState = TruthState.Confirmed)
    {
        _factRepository.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
    }

    #region Trusted route

    [Test]
    public async Task Refresh_WritesTheSummaryAndTheProvenanceStamp()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact();
        SeedFact(artifact.Id, "occupation", "smuggler");
        _aiClient.SummaryToReturn = "Captain Voss smuggles out of Black Harbor.";

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        var stored = (await _artifactRepository.GetByIdAsync(artifact.Id, CancellationToken.None))!;
        Assert.That(stored.Summary, Is.EqualTo("Captain Voss smuggles out of Black Harbor."));
        Assert.That(stored.SummaryRefreshedAt, Is.Not.Null, "the stamp is the provenance and the staleness gate");
        Assert.That(_usageRepository.Records.Single().OperationType, Is.EqualTo(AiOperationType.ArtifactSummary),
            "every AI call meters, under the operation type that sat dormant since MVP");
        Assert.That(_batchRepository.Batches, Is.Empty, "the trusted route files nothing for review");
    }

    [Test]
    public async Task Refresh_TruncatesAtTheColumnCeiling_InsteadOfFailing()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact();
        SeedFact(artifact.Id, "occupation", "smuggler");
        _aiClient.SummaryToReturn = new string('x', ArtifactSummaryService.MaxSummaryChars + 500);

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        var stored = (await _artifactRepository.GetByIdAsync(artifact.Id, CancellationToken.None))!;
        Assert.That(stored.Summary!.Length, Is.EqualTo(ArtifactSummaryService.MaxSummaryChars));
    }

    #endregion

    #region Authorization — the prompt is the leak surface

    [Test]
    [Category("Authorization")]
    public async Task PartyVisibleArtifact_PromptExcludesGmOnlyAndHiddenTruthFacts()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact(VisibilityScope.PartyVisible);
        SeedFact(artifact.Id, "occupation", "harbormaster");
        SeedFact(artifact.Id, "true allegiance", "the Silent Hand", VisibilityScope.GMOnly);
        SeedFact(artifact.Id, "secret plan", "burn the fleet", VisibilityScope.PartyVisible, TruthState.Hidden);

        await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        var prompt = _aiClient.LastRequest!.UserMessage;
        Assert.That(prompt, Does.Contain("harbormaster"));
        Assert.That(prompt, Does.Not.Contain("the Silent Hand"),
            "a GM-only fact in a party-visible artifact's generation context is a leak");
        Assert.That(prompt, Does.Not.Contain("burn the fleet"),
            "Hidden truth states are GM knowledge regardless of the fact's visibility scope");
    }

    [Test]
    [Category("Authorization")]
    public async Task GmOnlyArtifact_PromptIncludesGmFactsAndHiddenTruths()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact(VisibilityScope.GMOnly);
        SeedFact(artifact.Id, "true allegiance", "the Silent Hand", VisibilityScope.GMOnly);
        SeedFact(artifact.Id, "secret plan", "burn the fleet", VisibilityScope.PartyVisible, TruthState.Hidden);

        await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        var prompt = _aiClient.LastRequest!.UserMessage;
        Assert.That(prompt, Does.Contain("the Silent Hand"));
        Assert.That(prompt, Does.Contain("burn the fleet"),
            "a GM-only page is the GM's view, hidden truths included");
    }

    [Test]
    [Category("Authorization")]
    public async Task PartyVisibleArtifact_PromptDropsRelationshipsWhoseFarEndIsInvisible()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact(VisibilityScope.PartyVisible);
        SeedFact(artifact.Id, "occupation", "harbormaster");

        var secretFaction = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Faction,
            Name = "The Silent Hand",
            Visibility = VisibilityScope.GMOnly,
            Status = ArtifactStatus.Active,
            CreatedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        _artifactRepository.Seed(secretFaction);
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = artifact.Id,
            ArtifactBId = secretFaction.Id,
            Type = "MemberOf",
            TruthState = TruthState.Confirmed,
            // The ROW is party-visible; the far endpoint is not. Naming it is the leak.
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        });

        await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(_aiClient.LastRequest!.UserMessage, Does.Not.Contain("The Silent Hand"),
            "naming a GM-only artifact in a party-visible summary's basis is the same leak as quoting a GM-only fact");
    }

    #endregion

    #region Gates

    [Test]
    public async Task AlreadyRefreshedSinceTheRequest_SkipsWithoutSpending()
    {
        await SeedWorldAsync();
        var requestedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var artifact = SeedArtifact(summaryRefreshedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        SeedFact(artifact.Id, "occupation", "smuggler");

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, requestedAt, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_aiClient.CallCount, Is.Zero, "queued duplicates must not re-buy the generation");
    }

    [Test]
    public async Task BudgetExceeded_CompletesWithoutSpending()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact();
        SeedFact(artifact.Id, "occupation", "smuggler");
        _budgetGuard.Exceeded = true;

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(outcome.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_aiClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task NothingInScopeToSummarizeFrom_LeavesTheBirthSummaryStanding()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact(VisibilityScope.PartyVisible);
        // Only out-of-scope material exists: the party view has no basis at all.
        SeedFact(artifact.Id, "true allegiance", "the Silent Hand", VisibilityScope.GMOnly);

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_aiClient.CallCount, Is.Zero);
        var stored = (await _artifactRepository.GetByIdAsync(artifact.Id, CancellationToken.None))!;
        Assert.That(stored.Summary, Is.EqualTo("An old summary."));
    }

    [Test]
    public async Task ArchivedArtifact_KeepsItsLastSummary()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact(status: ArtifactStatus.Archived);
        SeedFact(artifact.Id, "occupation", "smuggler");

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_aiClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task WorldMismatch_IsNonTransient()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact();
        SeedFact(artifact.Id, "occupation", "smuggler");

        var outcome = await _service.RefreshAsync(artifact.Id, Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_aiClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task TransientAiFailure_ReportsTransient_SoTheMessageRedelivers()
    {
        await SeedWorldAsync();
        var artifact = SeedArtifact();
        SeedFact(artifact.Id, "occupation", "smuggler");
        _aiClient.ExceptionToThrow = new AiTimeoutException("timed out");

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(_usageRepository.Records.Single().Succeeded, Is.False, "the failed attempt still meters");
    }

    #endregion

    #region Review route

    [Test]
    public async Task SummaryReviewRequired_FilesAPendingProposalInsteadOfWriting()
    {
        await SeedWorldAsync(summaryReviewRequired: true);
        var artifact = SeedArtifact();
        SeedFact(artifact.Id, "occupation", "smuggler");
        _aiClient.SummaryToReturn = "Captain Voss smuggles out of Black Harbor.";

        var outcome = await _service.RefreshAsync(artifact.Id, WorldId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));

        var batch = _batchRepository.Batches.Single();
        Assert.That(batch.Kind, Is.EqualTo(ReviewBatchKinds.SummaryRefresh));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));

        var proposal = _proposalRepository.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.UpdateArtifact));
        Assert.That(proposal.TargetId, Is.EqualTo(artifact.Id));
        Assert.That(proposal.ProposedValueJson, Does.Contain("Captain Voss smuggles out of Black Harbor."));

        var stored = (await _artifactRepository.GetByIdAsync(artifact.Id, CancellationToken.None))!;
        Assert.That(stored.Summary, Is.EqualTo("An old summary."), "the gate means the GM decides, not the refresh");
        Assert.That(stored.SummaryRefreshedAt, Is.Not.Null,
            "the stamp still moves, or every queued duplicate would file another batch");
    }

    #endregion
}
