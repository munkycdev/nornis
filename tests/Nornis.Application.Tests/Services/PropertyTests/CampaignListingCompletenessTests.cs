using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 11: Campaign Listing Completeness and Exclusivity
///
/// For any authenticated user, listing their campaigns should return exactly the set of campaigns
/// where a CampaignMember record exists for that user — no more, no less — with each entry
/// including the user's CampaignRole in that campaign.
///
/// **Validates: Requirements 7.1, 7.2, 7.3**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-campaigns, Property 11: Campaign Listing Completeness and Exclusivity")]
public class CampaignListingCompletenessTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CampaignListingArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 11: Campaign Listing Completeness and Exclusivity")]
    public void ListForUser_ReturnsExactlyMemberCampaigns_WithCorrectRoles(CampaignListingScenario scenario)
    {
        // Arrange
        var memberRepo = new InMemoryCampaignMemberRepository();
        var campaignRepo = new InMemoryCampaignRepository(memberRepo);
        var service = new CampaignService(campaignRepo, memberRepo);

        // Seed all campaigns into the repository
        foreach (var campaign in scenario.AllCampaigns)
        {
            campaignRepo.CreateAsync(campaign, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed membership records
        foreach (var membership in scenario.Memberships)
        {
            memberRepo.CreateAsync(membership, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act
        var result = service.ListForUserAsync(scenario.TargetUserId, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation succeeds
        Assert.That(result.IsSuccess, Is.True, "ListForUserAsync should succeed.");

        var returnedCampaigns = result.Value!;

        // Determine expected campaign IDs (campaigns where the target user has a membership)
        var expectedCampaignIds = scenario.Memberships
            .Where(m => m.UserId == scenario.TargetUserId)
            .Select(m => m.CampaignId)
            .ToHashSet();

        var returnedCampaignIds = returnedCampaigns.Select(c => c.Campaign.Id).ToHashSet();

        // Assert completeness: all expected campaigns are returned
        Assert.That(returnedCampaignIds.IsSupersetOf(expectedCampaignIds),
            Is.True,
            "All campaigns where the user is a member must be returned.");

        // Assert exclusivity: no extra campaigns are returned
        Assert.That(returnedCampaignIds.IsSubsetOf(expectedCampaignIds),
            Is.True,
            "No campaigns where the user is NOT a member should be returned.");

        // Assert exact count matches
        Assert.That(returnedCampaigns.Count, Is.EqualTo(expectedCampaignIds.Count),
            "Returned campaign count must match the number of memberships.");

        // Assert each returned entry's Role matches the CampaignMember role
        foreach (var dto in returnedCampaigns)
        {
            var membership = scenario.Memberships.First(m =>
                m.CampaignId == dto.Campaign.Id && m.UserId == scenario.TargetUserId);

            Assert.That(dto.Role, Is.EqualTo(membership.Role),
                $"Role for campaign {dto.Campaign.Id} must match the membership role.");
        }
    }
}

/// <summary>
/// Scenario for the campaign listing completeness property test.
/// </summary>
public record CampaignListingScenario(
    Guid TargetUserId,
    IReadOnlyList<Campaign> AllCampaigns,
    IReadOnlyList<CampaignMember> Memberships);

/// <summary>
/// Custom FsCheck arbitraries for campaign listing completeness tests.
/// </summary>
public class CampaignListingArbitraries
{
    public static Arbitrary<CampaignListingScenario> CampaignListingScenarios()
    {
        var roleGen = Gen.Elements(CampaignRole.GM, CampaignRole.Player, CampaignRole.Observer);

        var scenarioGen =
            from targetUserId in ArbMap.Default.GeneratorFor<Guid>()
            from otherUserIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(3)
            from totalCampaignCount in Gen.Choose(1, 8)
            from campaignIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(totalCampaignCount)
            from memberCampaignCount in Gen.Choose(0, totalCampaignCount)
            from roles in roleGen.ArrayOf(memberCampaignCount)
            let allCampaigns = campaignIds.Select(id => new Campaign
            {
                Id = id,
                Name = $"Campaign {id.ToString()[..8]}",
                Description = null,
                GameSystem = null,
                CreatedByUserId = otherUserIds.Length > 0 ? otherUserIds[0] : Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }).ToList()
            let memberCampaigns = allCampaigns.Take(memberCampaignCount).ToList()
            let targetMemberships = memberCampaigns.Select((c, i) => new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = c.Id,
                UserId = targetUserId,
                Role = roles[i],
                JoinedAt = DateTimeOffset.UtcNow
            }).ToList()
            // Add some other-user memberships to non-member campaigns to verify exclusivity
            let otherMemberships = allCampaigns.Skip(memberCampaignCount)
                .SelectMany(c => otherUserIds.Take(1).Select(uid => new CampaignMember
                {
                    Id = Guid.NewGuid(),
                    CampaignId = c.Id,
                    UserId = uid,
                    Role = CampaignRole.GM,
                    JoinedAt = DateTimeOffset.UtcNow
                })).ToList()
            let allMemberships = targetMemberships.Concat(otherMemberships).ToList()
            select new CampaignListingScenario(
                targetUserId,
                allCampaigns.AsReadOnly(),
                allMemberships.AsReadOnly());

        return scenarioGen.ToArbitrary();
    }
}
