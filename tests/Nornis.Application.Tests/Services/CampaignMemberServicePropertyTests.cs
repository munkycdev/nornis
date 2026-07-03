using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Property-based tests for CampaignMemberService.
/// </summary>
[TestFixture]
public class CampaignMemberServicePropertyTests
{
    /// <summary>
    /// Validates: Requirements 4.2, 5.2
    ///
    /// Property 7: Non-GM Operations Are Denied
    /// For any campaign member with role Player or Observer, attempting to update campaign settings,
    /// add members, remove members, or change member roles should be denied with a 403 Forbidden response.
    /// </summary>
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonGmOperationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_UpdateCampaign(NonGmScenario scenario)
    {
        // Arrange
        var (campaignService, _, _, campaign) = SetupServices(scenario);

        var command = new UpdateCampaignCommand(
            campaign.Id,
            "Updated Name",
            "Updated Description",
            "Pathfinder 2e",
            scenario.ActingUserId);

        // Act
        var result = campaignService.UpdateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Non-GM should not be able to update campaign settings");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403), "Should return 403 Forbidden");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonGmOperationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_AddMember(NonGmScenario scenario)
    {
        // Arrange
        var (_, memberService, userRepo, campaign) = SetupServices(scenario);

        // Create a target user to add
        var targetUserId = Guid.NewGuid();
        userRepo.CreateAsync(new User
        {
            Id = targetUserId,
            Auth0SubjectId = $"auth0|{targetUserId}",
            Username = "Tavrin",
            Email = "tavrin@blackharbor.net",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None).GetAwaiter().GetResult();

        var command = new AddMemberCommand(
            campaign.Id,
            targetUserId,
            CampaignRole.Player,
            scenario.ActingUserId);

        // Act
        var result = memberService.AddMemberAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Non-GM should not be able to add members");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403), "Should return 403 Forbidden");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonGmOperationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_RemoveMember(NonGmScenario scenario)
    {
        // Arrange
        var (_, memberService, _, campaign) = SetupServices(scenario);

        // Act — try to remove the GM
        var result = memberService.RemoveMemberAsync(
            campaign.Id,
            scenario.GmUserId,
            scenario.ActingUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Non-GM should not be able to remove members");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403), "Should return 403 Forbidden");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonGmOperationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_UpdateRole(NonGmScenario scenario)
    {
        // Arrange
        var (_, memberService, _, campaign) = SetupServices(scenario);

        var command = new UpdateMemberRoleCommand(
            campaign.Id,
            scenario.GmUserId,
            CampaignRole.Observer,
            scenario.ActingUserId);

        // Act
        var result = memberService.UpdateRoleAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Non-GM should not be able to change member roles");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403), "Should return 403 Forbidden");
    }

    private static (CampaignService, CampaignMemberService, InMemoryUserRepository, Campaign) SetupServices(NonGmScenario scenario)
    {
        var campaignRepo = new InMemoryCampaignRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var userRepo = new InMemoryUserRepository();

        // Create the campaign
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

        // Add the GM member
        memberRepo.CreateAsync(new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = scenario.CampaignId,
            UserId = scenario.GmUserId,
            Role = CampaignRole.GM,
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
        }, CancellationToken.None).GetAwaiter().GetResult();

        // Add the non-GM acting member
        memberRepo.CreateAsync(new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = scenario.CampaignId,
            UserId = scenario.ActingUserId,
            Role = scenario.ActingRole,
            JoinedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None).GetAwaiter().GetResult();

        var campaignService = new CampaignService(campaignRepo, memberRepo);
        var memberService = new CampaignMemberService(memberRepo, userRepo);

        return (campaignService, memberService, userRepo, campaign);
    }
}

/// <summary>
/// Input model for non-GM operation scenarios.
/// </summary>
public record NonGmScenario(
    Guid CampaignId,
    Guid GmUserId,
    Guid ActingUserId,
    CampaignRole ActingRole);

/// <summary>
/// Custom FsCheck arbitraries for non-GM operation tests.
/// </summary>
public class NonGmOperationArbitraries
{
    public static Arbitrary<NonGmScenario> NonGmScenarios()
    {
        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from actingUserId in ArbMap.Default.GeneratorFor<Guid>()
            where actingUserId != gmUserId
            from role in Gen.Elements(CampaignRole.Player, CampaignRole.Observer)
            select new NonGmScenario(campaignId, gmUserId, actingUserId, role);

        return gen.ToArbitrary();
    }
}
