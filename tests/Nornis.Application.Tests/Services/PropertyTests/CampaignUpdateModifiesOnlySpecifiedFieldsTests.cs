using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 6: Campaign Update Modifies Only Specified Fields
///
/// For any existing campaign and any subset of updatable fields (Name, Description, GameSystem)
/// provided by a GM, after update, the specified fields should reflect the new values,
/// unspecified fields should remain unchanged, and UpdatedAt should be later than or equal
/// to the previous UpdatedAt.
///
/// **Validates: Requirements 4.1, 4.5**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-campaigns, Property 6: Campaign Update Modifies Only Specified Fields")]
public class CampaignUpdateModifiesOnlySpecifiedFieldsTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CampaignUpdateArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 6: Campaign Update Modifies Only Specified Fields")]
    public void CampaignUpdateModifiesOnlySpecifiedFields(CampaignUpdateTestInput input)
    {
        // Arrange: set up repos and create the campaign first
        var campaignRepo = new InMemoryCampaignRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var service = new CampaignService(campaignRepo, memberRepo);

        var createCommand = new CreateCampaignCommand(
            input.OriginalName,
            input.OriginalDescription,
            input.OriginalGameSystem,
            input.UserId);

        var createResult = service.CreateAsync(createCommand, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(createResult.IsSuccess, Is.True, "Campaign creation should succeed");

        var campaign = createResult.Value!;
        var previousUpdatedAt = campaign.UpdatedAt;

        // Capture original values before update
        var originalName = campaign.Name;
        var originalDescription = campaign.Description;
        var originalGameSystem = campaign.GameSystem;

        // Act: update with a subset of fields (some null = unchanged)
        var updateCommand = new UpdateCampaignCommand(
            campaign.Id,
            input.NewName,
            input.NewDescription,
            input.NewGameSystem,
            input.UserId);

        var updateResult = service.UpdateAsync(updateCommand, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(updateResult.IsSuccess, Is.True, "Campaign update should succeed for GM with valid input");

        var updated = updateResult.Value!;

        // Assert: specified fields reflect new values
        if (input.NewName is not null)
        {
            Assert.That(updated.Name, Is.EqualTo(input.NewName),
                "When Name is specified, it should be updated to the new value");
        }
        else
        {
            Assert.That(updated.Name, Is.EqualTo(originalName),
                "When Name is not specified (null), it should remain unchanged");
        }

        if (input.NewDescription is not null)
        {
            Assert.That(updated.Description, Is.EqualTo(input.NewDescription),
                "When Description is specified, it should be updated to the new value");
        }
        else
        {
            Assert.That(updated.Description, Is.EqualTo(originalDescription),
                "When Description is not specified (null), it should remain unchanged");
        }

        if (input.NewGameSystem is not null)
        {
            Assert.That(updated.GameSystem, Is.EqualTo(input.NewGameSystem),
                "When GameSystem is specified, it should be updated to the new value");
        }
        else
        {
            Assert.That(updated.GameSystem, Is.EqualTo(originalGameSystem),
                "When GameSystem is not specified (null), it should remain unchanged");
        }

        // Assert: UpdatedAt should be >= previous UpdatedAt
        Assert.That(updated.UpdatedAt, Is.GreaterThanOrEqualTo(previousUpdatedAt),
            "UpdatedAt should be later than or equal to the previous UpdatedAt");
    }
}

/// <summary>
/// Input model for campaign update property tests.
/// Contains original campaign data and the subset of fields to update.
/// </summary>
public record CampaignUpdateTestInput(
    string OriginalName,
    string? OriginalDescription,
    string? OriginalGameSystem,
    Guid UserId,
    string? NewName,
    string? NewDescription,
    string? NewGameSystem);

/// <summary>
/// Custom FsCheck arbitraries for campaign update tests.
/// Generates valid original campaigns and random subsets of update fields.
/// </summary>
public class CampaignUpdateArbitraries
{
    private static readonly char[] ValidNameChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    private static readonly char[] DescChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!-".ToCharArray();

    public static Arbitrary<CampaignUpdateTestInput> CampaignUpdateTestInputs()
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

        // For update fields: each can be null (not provided) or a valid value
        var updateNameGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            validNameGen.Select(n => (string?)n));

        var updateDescriptionGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 200)
            from chars in Gen.Elements(DescChars).ArrayOf(length)
            select (string?)new string(chars));

        var updateGameSystemGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("D&D 5e", "Pathfinder 2e", "Call of Cthulhu", "Shadowrun", "FATE", "Blades in the Dark"));

        var inputGen =
            from originalName in validNameGen
            from originalDescription in optionalDescriptionGen
            from originalGameSystem in optionalGameSystemGen
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from newName in updateNameGen
            from newDescription in updateDescriptionGen
            from newGameSystem in updateGameSystemGen
            select new CampaignUpdateTestInput(
                originalName,
                originalDescription,
                originalGameSystem,
                userId,
                newName,
                newDescription,
                newGameSystem);

        return inputGen.ToArbitrary();
    }
}
