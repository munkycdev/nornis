using System.Text.Json;
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

[TestFixture]
public class RelationshipBackfillServiceTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private InMemoryReviewProposalRepository _reviewProposalRepository = null!;
    private InMemorySourceReferenceRepository _sourceReferenceRepository = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private FakeRelationshipBackfillAiClient _aiClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private RelationshipBackfillService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _reviewProposalRepository = new InMemoryReviewProposalRepository();
        _sourceReferenceRepository = new InMemorySourceReferenceRepository();
        _aiUsageRecordRepository = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeRelationshipBackfillAiClient();
        _budgetGuard = new FakeAiBudgetGuard();

        var options = new ExtractionOptions
        {
            AiModel = "nornis-extract",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["nornis-extract"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 15.00m
                }
            }
        };

        _sut = new RelationshipBackfillService(
            _sourceRepository,
            _artifactRepository,
            _relationshipRepository,
            _reviewBatchRepository,
            _reviewProposalRepository,
            _sourceReferenceRepository,
            _aiUsageRecordRepository,
            _aiClient,
            _budgetGuard,
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<RelationshipBackfillService>.Instance);
    }

    private Source SeedProcessedSource(
        string? body = "The heist at the mint blew the Counterfeiters investigation wide open.",
        VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = body,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private Artifact SeedArtifact(string name, ArtifactType type, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = name,
            Type = type,
            Status = ArtifactStatus.Active,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepository.Seed(artifact);
        return artifact;
    }

    private static BackfillLinkProposal Link(string a, string b, string type, decimal confidence = 0.9m) => new()
    {
        ArtifactAName = a,
        ArtifactBName = b,
        Type = type,
        Rationale = $"The source shows {a} bearing on {b}.",
        Quote = "the heist at the mint",
        Confidence = confidence
    };

    private void SetAiLinks(params BackfillLinkProposal[] links)
    {
        _aiClient.Response = new RelationshipBackfillAiResponse
        {
            Links = links,
            InputTokens = 1000,
            OutputTokens = 200,
            TotalTokens = 1200,
            DurationMs = 900,
            Model = "nornis-extract"
        };
    }

    [Test]
    public async Task AlreadySweptSource_SkipsWithoutAiCall()
    {
        var source = SeedProcessedSource();
        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = source.Id,
            Kind = RelationshipBackfillService.BatchKind,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_aiClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task ExtractionBatch_DoesNotCountAsSwept()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        // The original extraction batch (Kind == null) must not block the sweep.
        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_aiClient.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task NonProcessedSource_Skips()
    {
        var source = SeedProcessedSource();
        source.ProcessingStatus = SourceProcessingStatus.Queued;

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_aiClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task MissingSource_NonTransient()
    {
        var result = await _sut.ProcessBackfillAsync(Guid.NewGuid(), WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
    }

    [Test]
    public async Task EmptyBody_CreatesCompletedEmptyBatch_MarkingSourceSwept()
    {
        var source = SeedProcessedSource(body: "   ");

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.Zero);
        Assert.That(_aiClient.CallCount, Is.Zero);

        var batch = _reviewBatchRepository.Batches.Single();
        Assert.That(batch.Kind, Is.EqualTo(RelationshipBackfillService.BatchKind));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Completed));

        // A second run now skips.
        var again = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);
        Assert.That(again.Type, Is.EqualTo(OutcomeType.Skipped));
    }

    [Test]
    public async Task BudgetExceeded_NonTransient_NoBatchCreated()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        _budgetGuard.Exceeded = true;

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_aiClient.CallCount, Is.Zero);
        // No batch: the source stays unswept so a later re-run picks it up.
        Assert.That(_reviewBatchRepository.Batches, Is.Empty);
        // Source is untouched — the sweep never mutates processing status.
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task NoStorylinesInWorld_CreatesEmptyBatch_WithoutAiCall()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Mint Heist", ArtifactType.Event); // events alone can't be linked

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.Zero);
        Assert.That(_aiClient.CallCount, Is.Zero);
        Assert.That(_reviewBatchRepository.Batches.Single().Kind, Is.EqualTo(RelationshipBackfillService.BatchKind));
    }

    [Test]
    public async Task HappyPath_PersistsPendingBatchWithProposalsAndQuotes()
    {
        var source = SeedProcessedSource();
        var storyline = SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        var evt = SeedArtifact("The Mint Heist", ArtifactType.Event);
        SetAiLinks(Link("The Mint Heist", "The Counterfeiters", "Advances"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.EqualTo(1));

        var batch = _reviewBatchRepository.Batches.Single();
        Assert.That(batch.Kind, Is.EqualTo(RelationshipBackfillService.BatchKind));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));
        Assert.That(batch.SourceId, Is.EqualTo(source.Id), "provenance must anchor to the original source");

        var proposal = _reviewProposalRepository.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.AddRelationship));
        Assert.That(proposal.TargetType, Is.EqualTo(ReviewTargetType.ArtifactRelationship));

        using var payload = JsonDocument.Parse(proposal.ProposedValueJson);
        Assert.That(payload.RootElement.GetProperty("artifactAId").GetGuid(), Is.EqualTo(evt.Id));
        Assert.That(payload.RootElement.GetProperty("artifactBId").GetGuid(), Is.EqualTo(storyline.Id));
        Assert.That(payload.RootElement.GetProperty("type").GetString(), Is.EqualTo("Advances"));
        Assert.That(payload.RootElement.GetProperty("visibility").GetString(), Is.EqualTo("PartyVisible"));

        var reference = _sourceReferenceRepository.References.Single();
        Assert.That(reference.SourceId, Is.EqualTo(source.Id));
        Assert.That(reference.TargetType, Is.EqualTo(SourceReferenceTargetType.ReviewProposal));
        Assert.That(reference.Quote, Is.EqualTo("the heist at the mint"));

        var usage = _aiUsageRecordRepository.Records.Single();
        Assert.That(usage.OperationType, Is.EqualTo(AiOperationType.RelationshipBackfill));
        Assert.That(usage.Succeeded, Is.True);
        Assert.That(usage.EstimatedCostUsd, Is.EqualTo(1000 * 2.50m / 1_000_000m + 200 * 15.00m / 1_000_000m));
    }

    [Test]
    public async Task UnknownArtifactName_ProposalDropped()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        SetAiLinks(Link("The Invented Event", "The Counterfeiters", "Advances"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.Zero);
        Assert.That(_reviewProposalRepository.Proposals, Is.Empty);
    }

    [Test]
    public async Task GmOnlyArtifact_NotLinkableFromPartyVisibleSource()
    {
        var source = SeedProcessedSource(visibility: VisibilityScope.PartyVisible);
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        SeedArtifact("The Secret Cabal", ArtifactType.Storyline, VisibilityScope.GMOnly);
        SetAiLinks(Link("The Counterfeiters", "The Secret Cabal", "PartOf"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.ProposalCount, Is.Zero);
        // And the GM-only storyline never reached the prompt either.
        Assert.That(_aiClient.LastRequest!.UserMessage, Does.Not.Contain("The Secret Cabal"));
    }

    [Test]
    public async Task ExistingRelationship_NotReproposed_EitherDirection()
    {
        var source = SeedProcessedSource();
        var storyline = SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        var evt = SeedArtifact("The Mint Heist", ArtifactType.Event);
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = storyline.Id, // reversed direction on purpose
            ArtifactBId = evt.Id,
            Type = "Advances",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SetAiLinks(Link("The Mint Heist", "The Counterfeiters", "Advances"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.ProposalCount, Is.Zero);
    }

    [Test]
    public async Task PartOf_ChildWithExistingParent_Dropped()
    {
        var source = SeedProcessedSource();
        var child = SeedArtifact("Side Investigation", ArtifactType.Storyline);
        var oldParent = SeedArtifact("The Old Arc", ArtifactType.Storyline);
        SeedArtifact("The New Arc", ArtifactType.Storyline);
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = child.Id,
            ArtifactBId = oldParent.Id,
            Type = "PartOf",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SetAiLinks(Link("Side Investigation", "The New Arc", "PartOf"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.ProposalCount, Is.Zero);
    }

    [Test]
    public async Task Advances_SwappedEndpoints_AreCorrected()
    {
        var source = SeedProcessedSource();
        var storyline = SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        var evt = SeedArtifact("The Mint Heist", ArtifactType.Event);
        // Model put the storyline in A and the event in B.
        SetAiLinks(Link("The Counterfeiters", "The Mint Heist", "Advances"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.ProposalCount, Is.EqualTo(1));
        using var payload = JsonDocument.Parse(_reviewProposalRepository.Proposals.Single().ProposedValueJson);
        Assert.That(payload.RootElement.GetProperty("artifactAId").GetGuid(), Is.EqualTo(evt.Id));
        Assert.That(payload.RootElement.GetProperty("artifactBId").GetGuid(), Is.EqualTo(storyline.Id));
    }

    [Test]
    public async Task DuplicateLinksInResponse_PersistedOnce()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        SeedArtifact("The Mint Heist", ArtifactType.Event);
        SetAiLinks(
            Link("The Mint Heist", "The Counterfeiters", "Advances"),
            Link("The Mint Heist", "The Counterfeiters", "Advances"));

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.ProposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TransientAiFailure_AbandonsForRedelivery_NoBatch()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        _aiClient.ExceptionToThrow = new HttpRequestException("503 service unavailable");

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(_reviewBatchRepository.Batches, Is.Empty);
        Assert.That(_aiUsageRecordRepository.Records.Single().Succeeded, Is.False);
    }

    [Test]
    public async Task TimeoutAiFailure_Transient()
    {
        var source = SeedProcessedSource();
        SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        _aiClient.ExceptionToThrow = new TimeoutException("timed out");

        var result = await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.TransientFailure));
    }

    [Test]
    public async Task PromptListsExistingLinks_AndSourceBody()
    {
        var source = SeedProcessedSource();
        var storyline = SeedArtifact("The Counterfeiters", ArtifactType.Storyline);
        var evt = SeedArtifact("The Mint Heist", ArtifactType.Event);
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = evt.Id,
            ArtifactBId = storyline.Id,
            Type = "Advances",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _sut.ProcessBackfillAsync(source.Id, WorldId, CancellationToken.None);

        var message = _aiClient.LastRequest!.UserMessage;
        Assert.That(message, Does.Contain("The Mint Heist —Advances→ The Counterfeiters"));
        Assert.That(message, Does.Contain(source.Body!));
        Assert.That(message, Does.Contain(source.Title));
    }
}
