using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 6: World Update Modifies Only Specified Fields
///
/// For any existing world and any subset of updatable fields (Name, Description, GameSystem)
/// provided by a GM, after update, the specified fields should reflect the new values,
/// unspecified fields should remain unchanged, and UpdatedAt should be later than or equal
/// to the previous UpdatedAt.
///
/// **Validates: Requirements 4.1, 4.5**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-worlds, Property 6: World Update Modifies Only Specified Fields")]
public class WorldUpdateModifiesOnlySpecifiedFieldsTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(WorldUpdateArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 6: World Update Modifies Only Specified Fields")]
    public void WorldUpdateModifiesOnlySpecifiedFields(WorldUpdateTestInput input)
    {
        // Arrange: set up repos and create the world first
        var worldRepo = new InMemoryWorldRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var service = new WorldService(worldRepo, memberRepo);

        var createCommand = new CreateWorldCommand(
            input.OriginalName,
            input.OriginalDescription,
            input.OriginalGameSystem,
            input.UserId);

        var createResult = service.CreateAsync(createCommand, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(createResult.IsSuccess, Is.True, "World creation should succeed");

        var world = createResult.Value!;
        var previousUpdatedAt = world.UpdatedAt;

        // Capture original values before update
        var originalName = world.Name;
        var originalDescription = world.Description;
        var originalGameSystem = world.GameSystem;

        // Act: update with a subset of fields (some null = unchanged)
        var updateCommand = new UpdateWorldCommand(
            world.Id,
            input.NewName,
            input.NewDescription,
            input.NewGameSystem,
            input.UserId);

        var updateResult = service.UpdateAsync(updateCommand, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(updateResult.IsSuccess, Is.True, "World update should succeed for GM with valid input");

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
/// Input model for world update property tests.
/// Contains original world data and the subset of fields to update.
/// </summary>
public record WorldUpdateTestInput(
    string OriginalName,
    string? OriginalDescription,
    string? OriginalGameSystem,
    Guid UserId,
    string? NewName,
    string? NewDescription,
    string? NewGameSystem);

/// <summary>
/// Custom FsCheck arbitraries for world update tests.
/// Generates valid original worlds and random subsets of update fields.
/// </summary>
public class WorldUpdateArbitraries
{
    private static readonly char[] ValidNameChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    private static readonly char[] DescChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!-".ToCharArray();

    public static Arbitrary<WorldUpdateTestInput> WorldUpdateTestInputs()
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
            select new WorldUpdateTestInput(
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
