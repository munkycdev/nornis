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
/// Pins the contract the carve created: derivation writes source CONTENT (the
/// persist-before-continue that keeps redelivery from re-buying vision calls) but never
/// source STATUS — a returned outcome is a verdict for the orchestrator's one mapping.
/// Handwriting moved to HandwritingTranscriptionPipelineTests with the code. Full
/// derivation behavior is covered end-to-end in ExtractionServiceDerivedTextTests.
/// </summary>
[TestFixture]
public class SourceTextDerivationTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private SourceTextDerivation _derivation = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _budgetGuard = new FakeAiBudgetGuard();
        _derivation = new SourceTextDerivation(
            _sourceRepository,
            _attachmentRepository,
            _blobStorage,
            new FakePdfTextExtractor(),
            new FakeImageReadingClient(),
            _budgetGuard,
            TestUsageRecorder.Wrap(new InMemoryAiUsageRecordRepository()),
            Options.Create(new ExtractionOptions
            {
                AiModel = "gpt-4o",
                AiEndpoint = "https://test.openai.azure.com/"
            }),
            NullLogger<SourceTextDerivation>.Instance);
    }

    private Source SeedHandwrittenSource()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Field notes",
            Body = null,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    [Test]
    public async Task NothingToDerive_ContinuesThePipeline()
    {
        var source = SeedHandwrittenSource();
        source.Type = SourceType.Upload;

        var outcome = await _derivation.DeriveAttachmentTextAsync(source, WorldId, CancellationToken.None);

        Assert.That(outcome, Is.Null, "no stored files — the typed body or the empty-body path decides");
        Assert.That(source.DerivedText, Is.Null);
    }
}
