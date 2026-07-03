using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 6: Cross-Campaign View Shows Only GM Campaigns
///
/// For any user who is a member of multiple campaigns with varying roles,
/// the by-campaign breakdown SHALL include only those campaigns where the user holds
/// the GM role. Campaigns where the user is a Player or Observer SHALL not appear.
///
/// **Validates: Requirements 4.2**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 6: Cross-campaign GM filtering")]
public class CrossCampaignGmFilteringTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CrossCampaignScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 6: Cross-campaign GM filtering")]
    public void GetByCampaignAsync_ReturnsOnlyGmCampaigns(CrossCampaignScenario scenario)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var campaignRepo = new InMemoryCampaignRepository();

        // Seed campaigns
        foreach (var campaign in scenario.Campaigns)
        {
            campaignRepo.CreateAsync(campaign, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed memberships
        foreach (var membership in scenario.Memberships)
        {
            memberRepo.CreateAsync(membership, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed AI usage records for all campaigns
        foreach (var record in scenario.UsageRecords)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        var costService = new CostService(
            aiUsageRepo,
            memberRepo,
            campaignRepo,
            NullLogger<CostService>.Instance);

        // Act
        var result = costService.GetByCampaignAsync(scenario.UserId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByCampaignAsync should succeed.");

        var campaignResults = result.Value!;
        var returnedCampaignIds = campaignResults.Select(c => c.CampaignId).ToHashSet();

        // Only GM campaigns should appear in results
        var gmCampaignIds = scenario.Memberships
            .Where(m => m.UserId == scenario.UserId && m.Role == CampaignRole.GM)
            .Select(m => m.CampaignId)
            .ToHashSet();

        // GM campaigns with usage records should be present
        var gmCampaignsWithRecords = gmCampaignIds
            .Where(id => scenario.UsageRecords.Any(r => r.CampaignId == id))
            .ToHashSet();

        Assert.That(returnedCampaignIds, Is.EquivalentTo(gmCampaignsWithRecords),
            "Results should contain exactly the GM campaigns that have usage records.");

        // No Player or Observer campaigns should appear
        var nonGmCampaignIds = scenario.Memberships
            .Where(m => m.UserId == scenario.UserId && m.Role != CampaignRole.GM)
            .Select(m => m.CampaignId)
            .ToHashSet();

        Assert.That(returnedCampaignIds.Intersect(nonGmCampaignIds), Is.Empty,
            "No Player or Observer campaigns should appear in results.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CrossCampaignScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 6: Cross-campaign GM filtering")]
    public void GetByCampaignAsync_ResolvesCorrectCampaignNames(CrossCampaignScenario scenario)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var campaignRepo = new InMemoryCampaignRepository();

        foreach (var campaign in scenario.Campaigns)
        {
            campaignRepo.CreateAsync(campaign, CancellationToken.None).GetAwaiter().GetResult();
        }

        foreach (var membership in scenario.Memberships)
        {
            memberRepo.CreateAsync(membership, CancellationToken.None).GetAwaiter().GetResult();
        }

        foreach (var record in scenario.UsageRecords)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        var costService = new CostService(
            aiUsageRepo,
            memberRepo,
            campaignRepo,
            NullLogger<CostService>.Instance);

        // Act
        var result = costService.GetByCampaignAsync(scenario.UserId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        var campaignNameMap = scenario.Campaigns.ToDictionary(c => c.Id, c => c.Name);

        foreach (var campaignResult in result.Value!)
        {
            Assert.That(campaignNameMap.ContainsKey(campaignResult.CampaignId), Is.True,
                "Returned campaign ID should exist in seeded campaigns.");
            Assert.That(campaignResult.CampaignName, Is.EqualTo(campaignNameMap[campaignResult.CampaignId]),
                "Campaign name should match the seeded campaign name.");
        }
    }
}

/// <summary>
/// Input model for cross-campaign GM filtering property tests.
/// </summary>
public record CrossCampaignScenario(
    Guid UserId,
    List<Campaign> Campaigns,
    List<CampaignMember> Memberships,
    List<AiUsageRecord> UsageRecords);

/// <summary>
/// Custom FsCheck arbitraries for cross-campaign GM filtering property tests.
/// Generates a user with memberships in multiple campaigns with mixed roles (GM, Player, Observer).
/// Ensures at least one GM campaign and at least one non-GM campaign exist.
/// </summary>
public class CrossCampaignScenarioArbitraries
{
    private static readonly string[] CampaignNames =
    [
        "Black Harbor Investigation",
        "Silver Key Mystery",
        "Missing Caravan",
        "Captain Voss Pursuit",
        "The Sunken Temple",
        "Ruins of Aldermoor",
        "The Crimson Accord"
    ];

    private static readonly AiOperationType[] OperationTypes =
    [
        AiOperationType.SourceExtraction,
        AiOperationType.ArtifactSummary,
        AiOperationType.AskLoremaster,
        AiOperationType.SourceExtractionRepair
    ];

    private static readonly string[] Models =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-3.5-turbo"
    ];

    public static Arbitrary<CrossCampaignScenario> CrossCampaignScenarios()
    {
        var gen =
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from gmCount in Gen.Choose(1, 3)
            from playerCount in Gen.Choose(1, 2)
            from observerCount in Gen.Choose(0, 2)
            let totalCampaigns = gmCount + playerCount + observerCount
            from campaignIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(totalCampaigns)
            let distinctCampaignIds = campaignIds.Distinct().ToArray()
            where distinctCampaignIds.Length >= gmCount + playerCount + observerCount
            from nameIndices in Gen.Elements(
                Enumerable.Range(0, CampaignNames.Length).ToArray())
                .ArrayOf(distinctCampaignIds.Length)
            let campaigns = distinctCampaignIds
                .Select((id, i) => new Campaign
                {
                    Id = id,
                    Name = CampaignNames[nameIndices[i] % CampaignNames.Length],
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    CreatedByUserId = userId
                }).ToList()
            let memberships = BuildMemberships(userId, distinctCampaignIds, gmCount, playerCount, observerCount)
            from recordsPerCampaign in Gen.Choose(1, 5)
            from records in GenUsageRecords(userId, distinctCampaignIds.ToList(), recordsPerCampaign)
            select new CrossCampaignScenario(userId, campaigns, memberships, records);

        return gen.ToArbitrary();
    }

    private static List<CampaignMember> BuildMemberships(
        Guid userId, Guid[] campaignIds, int gmCount, int playerCount, int observerCount)
    {
        var memberships = new List<CampaignMember>();
        var index = 0;

        // Assign GM roles first
        for (var i = 0; i < gmCount && index < campaignIds.Length; i++, index++)
        {
            memberships.Add(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignIds[index],
                UserId = userId,
                Role = CampaignRole.GM,
                DisplayName = "Kelda",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-14)
            });
        }

        // Assign Player roles
        for (var i = 0; i < playerCount && index < campaignIds.Length; i++, index++)
        {
            memberships.Add(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignIds[index],
                UserId = userId,
                Role = CampaignRole.Player,
                DisplayName = "Tavrin",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-10)
            });
        }

        // Assign Observer roles
        for (var i = 0; i < observerCount && index < campaignIds.Length; i++, index++)
        {
            memberships.Add(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignIds[index],
                UserId = userId,
                Role = CampaignRole.Observer,
                DisplayName = "Jorin",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-5)
            });
        }

        return memberships;
    }

    private static Gen<List<AiUsageRecord>> GenUsageRecords(
        Guid userId, List<Guid> campaignIds, int recordsPerCampaign)
    {
        var generators = campaignIds
            .SelectMany(campaignId =>
                Enumerable.Range(0, recordsPerCampaign)
                    .Select(_ => GenSingleRecord(userId, campaignId)));

        return generators.Aggregate(
            Gen.Constant(new List<AiUsageRecord>()),
            (accGen, recordGen) =>
                from acc in accGen
                from record in recordGen
                select new List<AiUsageRecord>(acc) { record });
    }

    private static Gen<AiUsageRecord> GenSingleRecord(Guid userId, Guid campaignId)
    {
        return
            from operationType in Gen.Elements(OperationTypes)
            from model in Gen.Elements(Models)
            from inputTokens in Gen.Choose(10, 5000)
            from outputTokens in Gen.Choose(10, 3000)
            from costCents in Gen.Choose(1, 500)
            select new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                UserId = userId,
                OperationType = operationType,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                EstimatedCostUsd = costCents / 100m,
                DurationMs = 150,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
    }
}
