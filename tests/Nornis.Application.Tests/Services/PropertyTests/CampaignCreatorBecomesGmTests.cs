using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 4: Campaign Creator Becomes GM
///
/// For any successfully created campaign, the creating user should be a CampaignMember
/// of that campaign with the GM role.
///
/// **Validates: Requirements 3.2**
/// </summary>
[TestFixture]
public class CampaignCreatorBecomesGmTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CreatorBecomesGmArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 4: Campaign Creator Becomes GM")]
    public void CampaignCreatorBecomesGM(CreatorBecomesGmInput input)
    {
        // Arrange
        var campaignRepo = new InMemoryCampaignRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var service = new CampaignService(campaignRepo, memberRepo);

        var command = new CreateCampaignCommand(
            input.Name,
            input.Description,
            input.GameSystem,
            input.CreatingUserId);

        // Act
        var result = service.CreateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - creation succeeds
        Assert.That(result.IsSuccess, Is.True, "Campaign creation should succeed for valid input");

        var campaign = result.Value!;

        // Assert - creator is a member of the campaign
        var member = memberRepo.Members.FirstOrDefault(m =>
            m.CampaignId == campaign.Id && m.UserId == input.CreatingUserId);

        Assert.That(member, Is.Not.Null,
            "The creating user must be a CampaignMember of the created campaign");

        // Assert - creator's role is GM
        Assert.That(member!.Role, Is.EqualTo(CampaignRole.GM),
            "The creating user's role must be GM");

        // Assert - member has a non-empty Id
        Assert.That(member.Id, Is.Not.EqualTo(Guid.Empty),
            "The CampaignMember record should have a non-empty Id");

        // Assert - member's CampaignId matches the campaign
        Assert.That(member.CampaignId, Is.EqualTo(campaign.Id),
            "The CampaignMember's CampaignId must match the created campaign's Id");
    }
}

/// <summary>
/// Input model for the Campaign Creator Becomes GM property test.
/// </summary>
public record CreatorBecomesGmInput(
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatingUserId);

/// <summary>
/// Custom FsCheck arbitraries for Campaign Creator Becomes GM test.
/// </summary>
public class CreatorBecomesGmArbitraries
{
    private static readonly char[] ValidNameChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    private static readonly char[] DescChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!-".ToCharArray();

    public static Arbitrary<CreatorBecomesGmInput> CreatorBecomesGmInputs()
    {
        var validNameGen =
            from length in Gen.Choose(1, 100)
            from chars in Gen.Elements(ValidNameChars).ArrayOf(length)
            let name = new string(chars).Trim()
            where !string.IsNullOrWhiteSpace(name) && name.Length >= 1 && name.Length <= 100
            select name;

        var optionalDescriptionGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 200)
            from chars in Gen.Elements(DescChars).ArrayOf(length)
            select (string?)new string(chars));

        var optionalGameSystemGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("D&D 5e", "Pathfinder 2e", "Call of Cthulhu", "Shadowrun", "FATE"));

        var inputGen =
            from name in validNameGen
            from description in optionalDescriptionGen
            from gameSystem in optionalGameSystemGen
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            select new CreatorBecomesGmInput(name, description, gameSystem, userId);

        return inputGen.ToArbitrary();
    }
}
