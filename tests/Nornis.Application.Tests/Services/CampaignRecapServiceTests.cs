using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The campaign recap's two-pass shape.
///
/// The load-bearing case is the second pass's scope. A party recap generated from a context
/// that held GM-only material is <em>derived</em> from it however carefully the prompt asks
/// the model to withhold — so the guarantee has to be that the second pass never sees it.
/// These tests assert on the filter each pass ran under, because that is where the guarantee
/// actually lives.
/// </summary>
[TestFixture]
public class CampaignRecapServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private static readonly Guid PlayerUserId = Guid.NewGuid();

    private InMemoryCampaignRepository _campaignRepository = null!;
    private InMemoryCampaignRecapRepository _recapRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private InMemoryAiUsageRecordRepository _usageRepository = null!;
    private FakeDigestAiClient _aiClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private RecordingCampaignRepository _recordingRepository = null!;
    private CampaignRecapService _sut = null!;

    /// <summary>
    /// Remembers every filter the service asked the rollup for, in order, so a test can see
    /// the GM pass and the party pass as two distinct scopes rather than one repeated.
    /// </summary>
    private sealed class RecordingCampaignRepository : InMemoryCampaignRepository
    {
        public List<VisibilityFilter> RollupFilters { get; } = [];

        public RecordingCampaignRepository(
            InMemorySourceRepository? sources = null, InMemoryCharacterRepository? characters = null)
            : base(sources, characters)
        {
        }

        public override Task<CampaignRollup> GetRollupAsync(
            Guid worldId, Guid campaignId, VisibilityFilter filter, int limit, CancellationToken cancellationToken = default)
        {
            RollupFilters.Add(filter);
            return base.GetRollupAsync(worldId, campaignId, filter, limit, cancellationToken);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _recordingRepository = new RecordingCampaignRepository(_sourceRepository);
        _campaignRepository = _recordingRepository;
        _recapRepository = new InMemoryCampaignRecapRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _usageRepository = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeDigestAiClient();
        _budgetGuard = new FakeAiBudgetGuard();

        _sut = new CampaignRecapService(
            _campaignRepository,
            _recapRepository,
            _artifactRepository,
            _sourceRepository,
            new RecordAssembler(_factRepository, _relationshipRepository, _referenceRepository),
            _aiClient,
            _budgetGuard,
            TestUsageRecorder.Wrap(_usageRepository),
            Options.Create(new LoremasterOptions { AiModel = "gpt-4o", AiEndpoint = "https://test.openai.azure.com/" }));
    }

    private Campaign SeedCampaign(Guid? worldId = null)
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Name = "The Missing Caravan",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId
        };
        _campaignRepository.Seed(campaign);
        return campaign;
    }

    /// <summary>Gives the rollup something to return so generation gets past the empty check.</summary>
    private Artifact SeedEvidencedArtifact(string name = "Captain Voss")
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = name,
            Status = ArtifactStatus.Active,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId,
            RowVersion = []
        };
        _artifactRepository.Seed(artifact);

        _recordingRepository.Rollup = new CampaignRollup(
            [new CampaignRollupArtifact(artifact.Id, name, ArtifactType.Character, null, ArtifactStatus.Active, 1)],
            1);

        return artifact;
    }

    #region Authorization

    [Test]
    public async Task Generation_is_refused_to_a_player()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();

        var result = await _sut.GenerateAsync(
            campaign.Id, WorldId, PlayerUserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(_aiClient.Requests, Is.Empty, "a refused caller still spent an AI call");
        });
    }

    [Test]
    public async Task Generation_refuses_a_campaign_from_another_world()
    {
        var foreign = SeedCampaign(worldId: Guid.NewGuid());

        var result = await _sut.GenerateAsync(
            foreign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
            Assert.That(_aiClient.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task Generation_stops_at_an_exhausted_budget()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();
        _budgetGuard.Exceeded = true;

        var result = await _sut.GenerateAsync(
            campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(_aiClient.Requests, Is.Empty);
        });
    }

    #endregion

    #region Two-pass scoping

    [Test]
    public async Task The_party_pass_never_sees_GM_only_material()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();

        await _sut.GenerateAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(_recordingRepository.RollupFilters, Has.Count.EqualTo(2),
            "the two renderings must come from two separately-scoped passes");

        var gmPass = _recordingRepository.RollupFilters[0];
        var partyPass = _recordingRepository.RollupFilters[1];

        Assert.Multiple(() =>
        {
            Assert.That(gmPass.Scopes, Contains.Item(VisibilityScope.GMOnly));
            Assert.That(partyPass.Scopes, Does.Not.Contain(VisibilityScope.GMOnly),
                "the party recap was generated from a context holding GM-only material");
            Assert.That(partyPass.Scopes, Does.Not.Contain(VisibilityScope.Private),
                "the party recap was generated from a context holding someone's private notes");
            Assert.That(partyPass.Scopes, Is.EquivalentTo([VisibilityScope.PartyVisible]));
        });
    }

    [Test]
    public async Task Both_renderings_are_stored_together()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();
        _aiClient.DigestToReturn = "## The story so far\nThe caravan is still missing.";

        var result = await _sut.GenerateAsync(
            campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        var stored = _recapRepository.Recaps.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(stored.CampaignId, Is.EqualTo(campaign.Id));
            Assert.That(stored.GmContentMarkdown, Is.Not.Empty);
            Assert.That(stored.PartyContentMarkdown, Is.Not.Empty);
            Assert.That(stored.Model, Is.EqualTo("gpt-4o"));
        });
    }

    [Test]
    public async Task Regenerating_replaces_rather_than_accumulates()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();

        await _sut.GenerateAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);
        await _sut.GenerateAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(_recapRepository.Recaps, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Each_pass_is_told_which_campaign_it_is_writing_about()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();

        await _sut.GenerateAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(_aiClient.Requests.Select(r => r.UserMessage),
            Is.All.Contains("The Missing Caravan"),
            "a pass with no campaign named would write the world digest instead");
    }

    #endregion

    #region Empty campaign

    [Test]
    public async Task A_campaign_whose_sources_evidence_nothing_is_refused_rather_than_invented()
    {
        var campaign = SeedCampaign();

        var result = await _sut.GenerateAsync(
            campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("empty_campaign"));
            Assert.That(_aiClient.Requests, Is.Empty, "a generation over nothing could only invent");
        });
    }

    #endregion

    #region Metering

    [Test]
    public async Task Every_pass_is_metered()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();

        await _sut.GenerateAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(_usageRepository.Records.Count(r => r.OperationType == AiOperationType.CampaignRecap),
            Is.EqualTo(_aiClient.Requests.Count));
    }

    [Test]
    public async Task A_failed_pass_is_still_metered()
    {
        var campaign = SeedCampaign();
        SeedEvidencedArtifact();
        _aiClient.ExceptionToThrow = new InvalidOperationException("upstream is down");

        var result = await _sut.GenerateAsync(
            campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
            Assert.That(_usageRepository.Records.Where(r => r.OperationType == AiOperationType.CampaignRecap),
                Is.Not.Empty, "a failed call left no trace in the ledger");
        });
    }

    #endregion
}
