using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 3: Campaign Creation Field Mapping
///
/// For any valid campaign creation input (Name 1–100 non-whitespace characters, optional Description,
/// optional GameSystem) and authenticated user, creating a campaign should return a Campaign with the
/// provided Name, Description, and GameSystem, a non-empty Id, CreatedByUserId equal to the acting
/// user's Id, and CreatedAt/UpdatedAt set to approximately the current time.
///
/// **Validates: Requirements 3.1, 3.4, 3.5, 3.6**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-campaigns, Property 3: Campaign Creation Field Mapping")]
public class CampaignCreationFieldMappingTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(FieldMappingArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 3: Campaign Creation Field Mapping")]
    public void CampaignCreation_MapsAllFieldsCorrectly(FieldMappingInput input)
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

        var beforeCreation = DateTimeOffset.UtcNow;

        // Act
        var result = service.CreateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        var afterCreation = DateTimeOffset.UtcNow;

        // Assert - operation should succeed
        Assert.That(result.IsSuccess, Is.True, "Campaign creation should succeed for valid input.");

        var campaign = result.Value!;

        // Assert - Name matches input
        Assert.That(campaign.Name, Is.EqualTo(input.Name),
            "Campaign Name must match the provided input.");

        // Assert - Description matches input
        Assert.That(campaign.Description, Is.EqualTo(input.Description),
            "Campaign Description must match the provided input.");

        // Assert - GameSystem matches input
        Assert.That(campaign.GameSystem, Is.EqualTo(input.GameSystem),
            "Campaign GameSystem must match the provided input.");

        // Assert - Non-empty Id
        Assert.That(campaign.Id, Is.Not.EqualTo(Guid.Empty),
            "Campaign Id must be a non-empty Guid.");

        // Assert - CreatedByUserId equals the acting user's Id
        Assert.That(campaign.CreatedByUserId, Is.EqualTo(input.CreatingUserId),
            "Campaign CreatedByUserId must equal the acting user's Id.");

        // Assert - CreatedAt is approximately the current time (within tolerance)
        Assert.That(campaign.CreatedAt, Is.GreaterThanOrEqualTo(beforeCreation),
            "Campaign CreatedAt must be at or after the time creation was initiated.");
        Assert.That(campaign.CreatedAt, Is.LessThanOrEqualTo(afterCreation),
            "Campaign CreatedAt must be at or before the time creation completed.");

        // Assert - UpdatedAt is approximately the current time
        Assert.That(campaign.UpdatedAt, Is.GreaterThanOrEqualTo(beforeCreation),
            "Campaign UpdatedAt must be at or after the time creation was initiated.");
        Assert.That(campaign.UpdatedAt, Is.LessThanOrEqualTo(afterCreation),
            "Campaign UpdatedAt must be at or before the time creation completed.");

        // Assert - CreatedAt and UpdatedAt are equal on creation
        Assert.That(campaign.CreatedAt, Is.EqualTo(campaign.UpdatedAt),
            "Campaign CreatedAt and UpdatedAt should be equal at creation time.");
    }
}

/// <summary>
/// Input model for campaign creation field mapping property tests.
/// </summary>
public record FieldMappingInput(
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatingUserId);

/// <summary>
/// Custom FsCheck arbitraries for campaign creation field mapping tests.
/// </summary>
public class FieldMappingArbitraries
{
    private static readonly char[] ValidNameChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    private static readonly char[] DescChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!-".ToCharArray();

    public static Arbitrary<FieldMappingInput> FieldMappingInputs()
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
            select new FieldMappingInput(name, description, gameSystem, userId);

        return inputGen.ToArbitrary();
    }
}
