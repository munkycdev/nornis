using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 9: Non-Member Access Denied
///
/// For any user who is not a CampaignMember of a given campaign, accessing any campaign-scoped
/// endpoint for that campaign should return HTTP 403 Forbidden, regardless of whether the campaign exists.
///
/// **Validates: Requirements 8.1, 8.2, 8.5**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-campaigns, Property 9: Non-Member Access Denied")]
public class NonMemberAccessDeniedTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberAccessArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 9: Non-Member Access Denied")]
    public void NonMember_GetCampaignById_Returns403(NonMemberScenario scenario)
    {
        // Arrange
        var (campaignService, _) = SetupServices(scenario);

        // Act — non-member attempts to get campaign details
        var result = campaignService.GetByIdAsync(
            scenario.CampaignId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to access campaign details.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access should return 403 Forbidden.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberAccessArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 9: Non-Member Access Denied")]
    public void NonMember_ListMembers_Returns403(NonMemberScenario scenario)
    {
        // Arrange
        var (_, memberService) = SetupServices(scenario);

        // Act — non-member attempts to list campaign members
        var result = memberService.ListMembersAsync(
            scenario.CampaignId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to list campaign members.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access should return 403 Forbidden.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberNonExistentCampaignArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 9: Non-Member Access Denied")]
    public void NonMember_GetNonExistentCampaign_Returns403(NonMemberNonExistentCampaignScenario scenario)
    {
        // Arrange — no campaign or members exist at all
        var campaignRepo = new InMemoryCampaignRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var userRepo = new InMemoryUserRepository();

        var campaignService = new CampaignService(campaignRepo, memberRepo);

        // Act — non-member attempts to access a campaign that doesn't exist
        var result = campaignService.GetByIdAsync(
            scenario.NonExistentCampaignId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should still be 403, not 404 (Req 8.5)
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to access non-existent campaign.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access to non-existent campaign should return 403, not 404.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberNonExistentCampaignArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 9: Non-Member Access Denied")]
    public void NonMember_ListMembersNonExistentCampaign_Returns403(NonMemberNonExistentCampaignScenario scenario)
    {
        // Arrange — no campaign or members exist at all
        var memberRepo = new InMemoryCampaignMemberRepository();
        var userRepo = new InMemoryUserRepository();

        var memberService = new CampaignMemberService(memberRepo, userRepo);

        // Act — non-member attempts to list members of a campaign that doesn't exist
        var result = memberService.ListMembersAsync(
            scenario.NonExistentCampaignId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should still be 403, not 404 (Req 8.5)
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to list members of non-existent campaign.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access to non-existent campaign members should return 403, not 404.");
    }

    private static (CampaignService, CampaignMemberService) SetupServices(NonMemberScenario scenario)
    {
        var campaignRepo = new InMemoryCampaignRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var userRepo = new InMemoryUserRepository();

        if (scenario.CampaignExists)
        {
            // Create the campaign with a GM who is NOT the non-member user
            var campaign = new Campaign
            {
                Id = scenario.CampaignId,
                Name = "Black Harbor Investigation",
                Description = "Investigating the missing caravan",
                GameSystem = "D&D 5e",
                CreatedByUserId = scenario.GmUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            campaignRepo.CreateAsync(campaign, CancellationToken.None).GetAwaiter().GetResult();

            // Add the GM as a member
            memberRepo.CreateAsync(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = scenario.CampaignId,
                UserId = scenario.GmUserId,
                Role = CampaignRole.GM,
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
            }, CancellationToken.None).GetAwaiter().GetResult();
        }

        // The non-member user is NOT added to the campaign

        var campaignService = new CampaignService(campaignRepo, memberRepo);
        var memberService = new CampaignMemberService(memberRepo, userRepo);

        return (campaignService, memberService);
    }
}

/// <summary>
/// Input model for non-member access scenarios where the campaign may or may not exist.
/// </summary>
public record NonMemberScenario(
    Guid CampaignId,
    Guid GmUserId,
    Guid NonMemberUserId,
    bool CampaignExists);

/// <summary>
/// Input model for non-member access to campaigns that definitely don't exist.
/// </summary>
public record NonMemberNonExistentCampaignScenario(
    Guid NonExistentCampaignId,
    Guid NonMemberUserId);

/// <summary>
/// Custom FsCheck arbitraries for non-member access denied tests.
/// Generates random users who are NOT members of a campaign.
/// </summary>
public class NonMemberAccessArbitraries
{
    public static Arbitrary<NonMemberScenario> NonMemberScenarios()
    {
        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from nonMemberUserId in ArbMap.Default.GeneratorFor<Guid>()
            where nonMemberUserId != gmUserId
            from campaignExists in Gen.Elements(true, false)
            select new NonMemberScenario(campaignId, gmUserId, nonMemberUserId, campaignExists);

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Custom FsCheck arbitraries for non-member access to non-existent campaigns.
/// </summary>
public class NonMemberNonExistentCampaignArbitraries
{
    public static Arbitrary<NonMemberNonExistentCampaignScenario> NonMemberNonExistentCampaignScenarios()
    {
        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from nonMemberUserId in ArbMap.Default.GeneratorFor<Guid>()
            select new NonMemberNonExistentCampaignScenario(campaignId, nonMemberUserId);

        return gen.ToArbitrary();
    }
}
