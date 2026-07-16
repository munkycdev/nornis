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
/// The extraction pipeline passes the source's campaign (when present) into the AI
/// request so the model can disambiguate recurring names across campaign eras.
/// </summary>
[TestFixture]
public partial class ExtractionServiceCampaignContextTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryCampaignRepository _campaignRepository = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private ExtractionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _campaignRepository = new InMemoryCampaignRepository();
        _aiClient = new FakeAiExtractionClient();

        var options = new ExtractionOptions
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

        _sut = new ExtractionService(
            _sourceRepository,
            _campaignRepository,
            new InMemoryReviewBatchRepository(),
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemoryAiUsageRecordRepository(),
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _aiClient,
            new FakeAiBudgetGuard(),
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<ExtractionService>.Instance);

        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals = [],
            InputTokens = 500,
            OutputTokens = 200,
            TotalTokens = 700,
            DurationMs = 1200,
            Model = "gpt-4o"
        });
    }

    private Source SeedQueuedSource(Guid? campaignId)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            CampaignId = campaignId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    [Test]
    public async Task ProcessExtractionAsync_SourceWithCampaign_PassesCampaignToAiRequest()
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = "Rise of Tiamat",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _campaignRepository.Seed(campaign);
        var source = SeedQueuedSource(campaign.Id);

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1));
        Assert.That(_aiClient.Requests[0].CampaignName, Is.EqualTo("Rise of Tiamat"));
        Assert.That(_aiClient.Requests[0].CampaignStatus, Is.EqualTo("Active"));
    }

    [Test]
    public async Task ProcessExtractionAsync_SourceWithoutCampaign_PassesNullCampaign()
    {
        var source = SeedQueuedSource(campaignId: null);

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1));
        Assert.That(_aiClient.Requests[0].CampaignName, Is.Null);
    }

    [Test]
    public async Task ProcessExtractionAsync_CampaignDeletedAfterQueueing_ProceedsWithoutContext()
    {
        // The source still points at a campaign id, but the campaign is gone.
        var source = SeedQueuedSource(Guid.NewGuid());

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_aiClient.Requests[0].CampaignName, Is.Null);
    }
}
