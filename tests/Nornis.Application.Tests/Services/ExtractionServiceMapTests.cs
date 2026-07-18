using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Map source extraction: place names + positions become review proposals —
/// CreateArtifact (with a pin block) for new places, AddPlacemark for places matching
/// existing Locations, with hallucination filtering and range clamping.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class ExtractionServiceMapTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceAttachmentRepository _attachmentRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryMapPlacemarkRepository _placemarkRepo = null!;
    private FakeBlobStorageService _blob = null!;
    private FakeMapExtractionClient _mapClient = null!;
    private FakeAiBudgetGuard _budget = null!;
    private ExtractionService _sut = null!;

    private Source _source = null!;
    private SourceAttachment _mapAttachment = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _attachmentRepo = new InMemorySourceAttachmentRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _placemarkRepo = new InMemoryMapPlacemarkRepository();
        _blob = new FakeBlobStorageService();
        _mapClient = new FakeMapExtractionClient();
        _budget = new FakeAiBudgetGuard();

        var options = new ExtractionOptions
        {
            AiModel = "nornis-extract",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["nornis-extract"] = new() { InputPerMillionTokensUsd = 2.50m, OutputPerMillionTokensUsd = 15.00m }
            }
        };

        _sut = new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            _batchRepo,
            _proposalRepo,
            new InMemorySourceReferenceRepository(),
            new InMemoryAiUsageRecordRepository(),
            _artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _attachmentRepo,
            _placemarkRepo,
            _blob,
            new FakePdfTextExtractor(),
            new FakeAiExtractionClient(),
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            _mapClient,
            _budget,
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<ExtractionService>.Instance);

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.Map,
            Title = "The Known Lands",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepo.Seed(_source);

        var blobPath = $"worlds/{WorldId}/sources/{_source.Id}/000-map.png";
        _blob.Blobs[blobPath] = (new byte[] { 9, 9, 9 }, "image/png");
        _mapAttachment = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = _source.Id, WorldId = WorldId,
            Kind = SourceAttachmentKind.MapImage, FileName = "map.png", ContentType = "image/png",
            SizeBytes = 3, BlobPath = blobPath, Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepo.Seed(_mapAttachment);
    }

    private Artifact SeedLocation(string name)
    {
        var a = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Location, Name = name,
            Visibility = VisibilityScope.PartyVisible, Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(a);
        return a;
    }

    private IReadOnlyList<ReviewProposal> ProposalsForSource() =>
        _batchRepo.Batches.Where(b => b.SourceId == _source.Id)
            .SelectMany(b => _proposalRepo.Proposals.Where(p => p.ReviewBatchId == b.Id))
            .ToList();

    [Test]
    public async Task UnmatchedPlace_BecomesCreateArtifactWithPinBlock()
    {
        _mapClient.PlacesToReturn = [new MapPlace("Ironhold", "fortress", 0.4m, 0.6m, 0.9m, null)];

        await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        var proposal = ProposalsForSource().Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.CreateArtifact));
        var payload = System.Text.Json.JsonSerializer.Deserialize<CreateArtifactPayload>(
            proposal.ProposedValueJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.That(payload.Name, Is.EqualTo("Ironhold"));
        Assert.That(payload.Type, Is.EqualTo("Location"));
        Assert.That(payload.MapPlacemark, Is.Not.Null);
        Assert.That(payload.MapPlacemark!.AttachmentId, Is.EqualTo(_mapAttachment.Id));
        Assert.That(payload.MapPlacemark.X, Is.EqualTo(0.4m));
    }

    [Test]
    public async Task PlaceMatchingExistingIdOrName_BecomesAddPlacemark()
    {
        var ironhold = SeedLocation("Ironhold");
        _mapClient.PlacesToReturn =
        [
            new MapPlace("Ironhold", "fortress", 0.4m, 0.6m, 0.9m, ironhold.Id), // by id
            new MapPlace("Black Harbor", null, 0.2m, 0.3m, 0.8m, null),          // unmatched
        ];
        SeedLocation("Black Harbor"); // now matchable by unique name too
        _mapClient.PlacesToReturn =
        [
            new MapPlace("Ironhold", "fortress", 0.4m, 0.6m, 0.9m, ironhold.Id),
            new MapPlace("Black Harbor", null, 0.2m, 0.3m, 0.8m, null),
        ];

        await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        var proposals = ProposalsForSource();
        Assert.That(proposals.Count(p => p.ChangeType == ReviewChangeType.AddPlacemark), Is.EqualTo(2),
            "id-matched and unique-name-matched both become AddPlacemark");
    }

    [Test]
    public async Task OutOfRangeCoordinates_AreDropped()
    {
        _mapClient.PlacesToReturn =
        [
            new MapPlace("Valid", null, 0.5m, 0.5m, null, null),
            new MapPlace("Off Map", null, 1.5m, 0.5m, null, null),
        ];

        await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        var proposals = ProposalsForSource();
        Assert.That(proposals, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HallucinatedExistingArtifactId_IsIgnored_FallsToCreate()
    {
        // The id is not in the offered context → treated as no match → CreateArtifact.
        _mapClient.PlacesToReturn = [new MapPlace("Ghost Keep", null, 0.5m, 0.5m, null, Guid.NewGuid())];

        await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        var proposal = ProposalsForSource().Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.CreateArtifact));
    }

    [Test]
    public async Task ExistingPlacemark_IsSkipped()
    {
        var ironhold = SeedLocation("Ironhold");
        _placemarkRepo.Seed(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceAttachmentId = _mapAttachment.Id,
            ArtifactId = ironhold.Id, X = 0.4m, Y = 0.6m,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        _mapClient.PlacesToReturn = [new MapPlace("Ironhold", "fortress", 0.4m, 0.6m, 0.9m, ironhold.Id)];

        await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        Assert.That(ProposalsForSource(), Is.Empty, "already-pinned place produces no proposal");
    }

    [Test]
    public async Task EmptyPlaces_ProducesCompletedEmptyBatch()
    {
        _mapClient.PlacesToReturn = [];

        var outcome = await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        var stored = (await _sourceRepo.GetByIdAsync(_source.Id, CancellationToken.None))!;
        Assert.That(stored.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
        Assert.That(_batchRepo.Batches.Any(b => b.SourceId == _source.Id), Is.True);
    }

    [Test]
    public async Task NoMapImage_FilesEmptyBatch()
    {
        _attachmentRepo.DeleteAsync(_mapAttachment.Id).GetAwaiter().GetResult();

        var outcome = await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_mapClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task BudgetBlocked_FailsSource()
    {
        _budget.Exceeded = true;
        _mapClient.PlacesToReturn = [new MapPlace("Ironhold", null, 0.5m, 0.5m, null, null)];

        var outcome = await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_mapClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task ExistingBatch_SkipsReextraction()
    {
        _batchRepo.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceId = _source.Id,
            Status = ReviewBatchStatus.Completed, CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();

        var outcome = await _sut.ProcessExtractionAsync(_source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(_mapClient.CallCount, Is.Zero, "idempotency: existing batch means no re-extraction");
    }
}
