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
/// Pins the contract the carve created: the pipeline returns a verdict — no-image,
/// proposals, or a failure outcome — and by construction cannot move source status (it
/// holds no source repository at all). The full map behavior (matching, dedup, clamping,
/// persistence) is covered end-to-end in ExtractionServiceMapTests through the
/// orchestrator, which is also where these paths' status transitions are asserted.
/// </summary>
[TestFixture]
public class MapExtractionPipelineTests
{
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private FakeMapExtractionClient _mapClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private MapExtractionPipeline _pipeline = null!;
    private Source _source = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _mapClient = new FakeMapExtractionClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _pipeline = new MapExtractionPipeline(
            _attachmentRepository,
            new InMemoryMapPlacemarkRepository(),
            new InMemoryArtifactRepository(),
            _blobStorage,
            _mapClient,
            _budgetGuard,
            TestUsageRecorder.Wrap(new InMemoryAiUsageRecordRepository()),
            Options.Create(new ExtractionOptions
            {
                AiModel = "gpt-4o",
                AiEndpoint = "https://test.openai.azure.com/",
                MaxParseRetryAttempts = 1
            }),
            NullLogger<MapExtractionPipeline>.Instance);

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.Map,
            Title = "The Reach",
            Body = null,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
    }

    private SourceAttachment SeedStoredMapImage()
    {
        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = _source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.MapImage,
            FileName = "map.png",
            ContentType = "image/png",
            SizeBytes = 3,
            BlobPath = $"worlds/{WorldId}/sources/{_source.Id}/map.png",
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepository.Seed(attachment);
        return attachment;
    }

    [Test]
    public async Task NoStoredMapImage_ReturnsNoMapImage_WithoutSpendingAnything()
    {
        var result = await _pipeline.ExtractAsync(_source, WorldId, CancellationToken.None);

        Assert.That(result.Response, Is.Null);
        Assert.That(result.Failure, Is.Null);
        Assert.That(_mapClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task BudgetExceeded_ReturnsFailedVerdict_BeforeTheAiCall()
    {
        SeedStoredMapImage();
        _budgetGuard.Exceeded = true;

        var result = await _pipeline.ExtractAsync(_source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.Failure.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_mapClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task MissingMapBlob_ReturnsFailedVerdict_NamingTheProblem()
    {
        SeedStoredMapImage(); // attachment row exists, but no blob is seeded behind it

        var result = await _pipeline.ExtractAsync(_source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.Failure.ErrorCategory, Is.EqualTo(ErrorCategories.ValidationFailure));
        Assert.That(_mapClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task StoredImage_ReturnsExtractedProposals_ForTheOrchestratorToPersist()
    {
        var attachment = SeedStoredMapImage();
        _blobStorage.Blobs[attachment.BlobPath] = ([1, 2, 3], "image/png");
        _mapClient.PlacesToReturn = [new MapPlace("Ironhold", "fortress", 0.4m, 0.6m, 0.9m, null)];

        var result = await _pipeline.ExtractAsync(_source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Null);
        Assert.That(result.Response, Is.Not.Null);
        var proposal = result.Response!.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo("CreateArtifact"));
        Assert.That(proposal.Quote, Is.EqualTo("Ironhold"));
    }
}
