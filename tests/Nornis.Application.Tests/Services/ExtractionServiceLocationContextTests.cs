using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Ai;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The extraction pipeline carries the party's last known location forward: the nearest
/// prior timeline source with accepted Location links supplies a RecentLocationContext so
/// the model can resolve place references the source never spells out. Pivoting on the
/// source's own OccurredAt keeps backfilled imports anchored to their place in the story,
/// and visibility scoping keeps GM-only whereabouts out of party-visible extractions.
/// </summary>
[TestFixture]
public class ExtractionServiceLocationContextTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly DateTimeOffset Day5 = new(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day10 = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day15 = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day20 = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);

    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private ExtractionOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _aiClient = new FakeAiExtractionClient();

        _options = new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        };

        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals = [],
            Usage = new AiUsage
            {
                InputTokens = 500,
                OutputTokens = 200,
                TotalTokens = 700,
                DurationMs = 1200,
                Model = "gpt-4o"
            }
        });
    }

    private ExtractionService CreateSut()
    {
        var usageRecorder = TestUsageRecorder.Wrap(new InMemoryAiUsageRecordRepository());
        var options = Options.Create(_options);
        var budgetGuard = new FakeAiBudgetGuard();
        var attachmentRepository = new InMemorySourceAttachmentRepository();
        var blobStorage = new FakeBlobStorageService();
        var mapPipeline = new MapExtractionPipeline(
            attachmentRepository,
            new InMemoryMapPlacemarkRepository(),
            _artifactRepository,
            blobStorage,
            new FakeMapExtractionClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<MapExtractionPipeline>.Instance);
        var textDerivation = new SourceTextDerivation(
            _sourceRepository,
            attachmentRepository,
            blobStorage,
            new FakePdfTextExtractor(),
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<SourceTextDerivation>.Instance);

        return new ExtractionService(
            _sourceRepository,
            new InMemoryCampaignRepository(),
            new InMemoryReviewBatchRepository(),
            new InMemoryReviewProposalRepository(),
            _referenceRepository,
            usageRecorder,
            _artifactRepository,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _aiClient,
            mapPipeline,
            textDerivation,
            budgetGuard,
            new FakeUnitOfWork(),
            options,
            NullLogger<ExtractionService>.Instance,
            passageRetriever: NoOpReferencePassageRetriever.Instance,
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance);
    }

    private Source SeedSource(
        string title,
        DateTimeOffset? occurredAt,
        SourceType type = SourceType.SessionNote,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        Guid? campaignId = null,
        SourceProcessingStatus processingStatus = SourceProcessingStatus.Processed)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            CampaignId = campaignId,
            Type = type,
            Title = title,
            Body = "Something happened.",
            Visibility = visibility,
            ProcessingStatus = processingStatus,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt ?? DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private Artifact SeedArtifact(
        string name,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        ArtifactStatus status = ArtifactStatus.Active,
        ArtifactType type = ArtifactType.Location)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = type,
            Name = name,
            Summary = $"{name} summary",
            Visibility = visibility,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepository.Seed(artifact);
        return artifact;
    }

    private void Link(Source source, Artifact artifact) =>
        _referenceRepository.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = SourceReferenceTargetType.Artifact,
            TargetId = artifact.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

    private async Task<ParsedLocationContext?> ExtractAndGetLocationContext(Source source)
    {
        var outcome = await CreateSut().ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1));
        return ExtractionPromptReader.Parse(_aiClient.Requests[0].UserMessage).RecentLocations;
    }

    [Test]
    public async Task PriorSourceWithLocationLinks_PassesLocationContext()
    {
        var prior = SeedSource("Session 4", Day10);
        var harbor = SeedArtifact("Black Harbor");
        Link(prior, harbor);
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Not.Null);
        Assert.That(context!.SourceTitle, Is.EqualTo("Session 4"));
        Assert.That(context.OccurredAtDate, Is.EqualTo(DateOnly.FromDateTime(Day10.Date)));
        Assert.That(context.Locations, Has.Count.EqualTo(1));
        Assert.That(context.Locations[0].Id, Is.EqualTo(harbor.Id));
        Assert.That(context.Locations[0].Name, Is.EqualTo("Black Harbor"));
        Assert.That(context.Locations[0].Summary, Is.EqualTo("Black Harbor summary"));
    }

    [Test]
    public async Task NearestPriorSourceWithLocations_Wins()
    {
        var older = SeedSource("Session 3", Day5);
        Link(older, SeedArtifact("Ironhold"));
        var nearer = SeedSource("Session 4", Day10);
        Link(nearer, SeedArtifact("Black Harbor"));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context!.SourceTitle, Is.EqualTo("Session 4"));
        Assert.That(context.Locations.Select(l => l.Name), Is.EqualTo(["Black Harbor"]));
    }

    [Test]
    public async Task LocationlessNearerPrior_FallsBackToOlderPrior()
    {
        var older = SeedSource("Session 3", Day5);
        Link(older, SeedArtifact("Ironhold"));
        // The nearer session referenced only a character — no location signal.
        var nearer = SeedSource("Session 4", Day10);
        Link(nearer, SeedArtifact("Captain Voss", type: ArtifactType.Character));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context!.SourceTitle, Is.EqualTo("Session 3"));
        Assert.That(context.Locations.Select(l => l.Name), Is.EqualTo(["Ironhold"]));
    }

    [Test]
    public async Task NoPriorSourceWithLocations_PassesNull()
    {
        SeedSource("Session 4", Day10);
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }

    [Test]
    public async Task LaterSources_NeverLeakBackward()
    {
        // A backfilled note is extracted for a moment BEFORE an already-recorded session:
        // that later session's location must not bleed into the past.
        var later = SeedSource("Session 6", Day20);
        Link(later, SeedArtifact("Black Harbor"));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }

    [Test]

    [Category("Authorization")]
    public async Task GmOnlyPriorSource_InvisibleToPartyVisibleExtraction()
    {
        var gmPrior = SeedSource("GM aside", Day10, visibility: VisibilityScope.GMOnly);
        Link(gmPrior, SeedArtifact("Hidden Sanctum"));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }

    [Test]

    [Category("Authorization")]
    public async Task GmOnlyLocation_ExcludedFromPartyVisibleContext()
    {
        var prior = SeedSource("Session 4", Day10);
        Link(prior, SeedArtifact("Hidden Sanctum", visibility: VisibilityScope.GMOnly));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }

    [Test]
    public async Task ArchivedLocation_Excluded()
    {
        var prior = SeedSource("Session 4", Day10);
        Link(prior, SeedArtifact("Razed Village", status: ArtifactStatus.Archived));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }

    [Test]
    public async Task OtherCampaignPrior_Ignored_CampaignlessPrior_Included()
    {
        var campaignId = Guid.NewGuid();
        var otherCampaignPrior = SeedSource("Era Two Session", Day10, campaignId: Guid.NewGuid());
        Link(otherCampaignPrior, SeedArtifact("Wrong Era Keep"));
        var campaignlessPrior = SeedSource("Untagged Session", Day5);
        Link(campaignlessPrior, SeedArtifact("Ironhold"));
        var current = SeedSource(
            "Session 5", Day15, campaignId: campaignId, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context!.SourceTitle, Is.EqualTo("Untagged Session"));
        Assert.That(context.Locations.Select(l => l.Name), Is.EqualTo(["Ironhold"]));
    }

    [Test]
    public async Task LookbackCap_BoundsHowFarBackTheWalkGoes()
    {
        _options.LocationContextLookbackSources = 2;
        var bearing = SeedSource("Session 2", Day5);
        Link(bearing, SeedArtifact("Ironhold"));
        SeedSource("Session 3", Day10);
        SeedSource("Session 4", Day15);
        var current = SeedSource("Session 5", Day20, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }

    [Test]
    public async Task LookbackZero_DisablesLocationContext()
    {
        _options.LocationContextLookbackSources = 0;
        var prior = SeedSource("Session 4", Day10);
        Link(prior, SeedArtifact("Black Harbor"));
        var current = SeedSource("Session 5", Day15, processingStatus: SourceProcessingStatus.Queued);

        var context = await ExtractAndGetLocationContext(current);

        Assert.That(context, Is.Null);
    }
}
