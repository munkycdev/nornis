using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Unit tests for CostService cross-campaign functionality (GetByCampaignAsync).
///
/// Validates: Requirements 4.1, 4.2, 4.3
/// </summary>
[TestFixture]
public class CostServiceCrossCampaignTests
{
    private InMemoryAiUsageRecordRepository _aiUsageRepo = null!;
    private InMemoryCampaignMemberRepository _memberRepo = null!;
    private InMemoryCampaignRepository _campaignRepo = null!;
    private CostService _costService = null!;

    // User IDs
    private static readonly Guid KeldaUserId = Guid.NewGuid();

    // Campaign IDs
    private static readonly Guid BlackHarborCampaignId = Guid.NewGuid();
    private static readonly Guid SilverKeyCampaignId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _aiUsageRepo = new InMemoryAiUsageRecordRepository();
        _memberRepo = new InMemoryCampaignMemberRepository();
        _campaignRepo = new InMemoryCampaignRepository(_memberRepo);

        _costService = new CostService(
            _aiUsageRepo,
            _memberRepo,
            _campaignRepo,
            NullLogger<CostService>.Instance);
    }

    [Test]
    public async Task GetByCampaignAsync_returns_only_campaigns_where_user_is_gm()
    {
        // Arrange — Kelda is GM in "Black Harbor Investigation", Player in "Silver Key Mystery"
        await SeedCampaign(BlackHarborCampaignId, "Black Harbor Investigation");
        await SeedCampaign(SilverKeyCampaignId, "Silver Key Mystery");

        await SeedMembership(BlackHarborCampaignId, KeldaUserId, CampaignRole.GM, "Kelda");
        await SeedMembership(SilverKeyCampaignId, KeldaUserId, CampaignRole.Player, "Kelda");

        // Seed usage records in both campaigns
        await SeedUsageRecord(BlackHarborCampaignId, KeldaUserId, inputTokens: 100, outputTokens: 50);
        await SeedUsageRecord(SilverKeyCampaignId, KeldaUserId, inputTokens: 200, outputTokens: 100);

        // Act
        var result = await _costService.GetByCampaignAsync(KeldaUserId, CancellationToken.None);

        // Assert — only Black Harbor (GM campaign) appears
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].CampaignId, Is.EqualTo(BlackHarborCampaignId));
        Assert.That(result.Value[0].CampaignName, Is.EqualTo("Black Harbor Investigation"));
    }

    [Test]
    public async Task GetByCampaignAsync_user_with_no_gm_campaigns_returns_empty_list()
    {
        // Arrange — Kelda is Player in both campaigns, GM in neither
        await SeedCampaign(BlackHarborCampaignId, "Black Harbor Investigation");
        await SeedCampaign(SilverKeyCampaignId, "Silver Key Mystery");

        await SeedMembership(BlackHarborCampaignId, KeldaUserId, CampaignRole.Player, "Kelda");
        await SeedMembership(SilverKeyCampaignId, KeldaUserId, CampaignRole.Observer, "Kelda");

        // Seed usage records
        await SeedUsageRecord(BlackHarborCampaignId, KeldaUserId, inputTokens: 500, outputTokens: 250);
        await SeedUsageRecord(SilverKeyCampaignId, KeldaUserId, inputTokens: 300, outputTokens: 150);

        // Act
        var result = await _costService.GetByCampaignAsync(KeldaUserId, CancellationToken.None);

        // Assert — empty list, not an error
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task GetByCampaignAsync_campaign_names_resolved_correctly()
    {
        // Arrange — Kelda is GM in both campaigns
        await SeedCampaign(BlackHarborCampaignId, "Black Harbor Investigation");
        await SeedCampaign(SilverKeyCampaignId, "Silver Key Mystery");

        await SeedMembership(BlackHarborCampaignId, KeldaUserId, CampaignRole.GM, "Kelda");
        await SeedMembership(SilverKeyCampaignId, KeldaUserId, CampaignRole.GM, "Kelda");

        // Seed usage records in both campaigns
        await SeedUsageRecord(BlackHarborCampaignId, KeldaUserId, inputTokens: 100, outputTokens: 50);
        await SeedUsageRecord(SilverKeyCampaignId, KeldaUserId, inputTokens: 200, outputTokens: 100);

        // Act
        var result = await _costService.GetByCampaignAsync(KeldaUserId, CancellationToken.None);

        // Assert — both campaigns returned with correct names
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));

        var campaignNames = result.Value!.Select(c => c.CampaignName).OrderBy(n => n).ToList();
        Assert.That(campaignNames, Is.EqualTo(new[] { "Black Harbor Investigation", "Silver Key Mystery" }));

        // Verify IDs match the correct names
        var blackHarbor = result.Value!.First(c => c.CampaignId == BlackHarborCampaignId);
        Assert.That(blackHarbor.CampaignName, Is.EqualTo("Black Harbor Investigation"));

        var silverKey = result.Value!.First(c => c.CampaignId == SilverKeyCampaignId);
        Assert.That(silverKey.CampaignName, Is.EqualTo("Silver Key Mystery"));
    }

    #region Helpers

    private async Task SeedCampaign(Guid campaignId, string name)
    {
        await _campaignRepo.CreateAsync(new Campaign
        {
            Id = campaignId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedByUserId = KeldaUserId
        });
    }

    private async Task SeedMembership(Guid campaignId, Guid userId, CampaignRole role, string displayName)
    {
        await _memberRepo.CreateAsync(new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            UserId = userId,
            Role = role,
            DisplayName = displayName,
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });
    }

    private async Task SeedUsageRecord(Guid campaignId, Guid userId, int inputTokens, int outputTokens)
    {
        await _aiUsageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            UserId = userId,
            OperationType = AiOperationType.SourceExtraction,
            Model = "gpt-4o",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            EstimatedCostUsd = (inputTokens * 0.005m + outputTokens * 0.015m) / 1000m,
            DurationMs = 500,
            Succeeded = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
    }

    #endregion
}
