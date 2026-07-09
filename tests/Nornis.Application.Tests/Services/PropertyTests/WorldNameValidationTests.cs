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
/// Property 5: World Name Validation Rejects Invalid Names
///
/// For any string that is null, empty, composed entirely of whitespace, or longer than 100 characters,
/// both world creation and world update operations should reject the input with a validation error
/// (status 400).
///
/// **Validates: Requirements 3.3, 4.6**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-worlds, Property 5: World Name Validation Rejects Invalid Names")]
public class WorldNameValidationTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidNameArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 5: World Name Validation Rejects Invalid Names - CreateAsync rejects invalid names")]
    public void CreateAsync_RejectsInvalidNames(InvalidNameInput input)
    {
        // Arrange
        var worldRepo = new InMemoryWorldRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var service = new WorldService(worldRepo, memberRepo);

        var command = new CreateWorldCommand(
            input.InvalidName!,
            "Black Harbor Investigation",
            "D&D 5e",
            input.UserId);

        // Act
        var result = service.CreateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should fail with status 400
        Assert.That(result.IsSuccess, Is.False,
            $"World creation should reject invalid name: \"{input.InvalidName ?? "(null)"}\"");
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400),
            "Validation error should return status code 400.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidNameArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 5: World Name Validation Rejects Invalid Names - UpdateAsync rejects invalid names")]
    public void UpdateAsync_RejectsInvalidNames(InvalidNameInput input)
    {
        // Arrange - first create a valid world as GM
        var worldRepo = new InMemoryWorldRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var service = new WorldService(worldRepo, memberRepo);

        var createCommand = new CreateWorldCommand(
            "Black Harbor Investigation",
            "A mystery in Black Harbor",
            "D&D 5e",
            input.UserId);

        var createResult = service.CreateAsync(createCommand, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(createResult.IsSuccess, Is.True, "Setup: valid world creation should succeed.");

        var world = createResult.Value!;

        // Act - attempt update with invalid name
        var updateCommand = new UpdateWorldCommand(
            world.Id,
            input.InvalidName,
            null,
            null,
            input.UserId);

        var updateResult = service.UpdateAsync(updateCommand, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should fail with status 400
        Assert.That(updateResult.IsSuccess, Is.False,
            $"World update should reject invalid name: \"{input.InvalidName ?? "(null)"}\"");
        Assert.That(updateResult.Error, Is.Not.Null);
        Assert.That(updateResult.Error!.StatusCode, Is.EqualTo(400),
            "Validation error should return status code 400.");
    }
}

/// <summary>
/// Input model for world name validation property tests.
/// </summary>
public record InvalidNameInput(
    string? InvalidName,
    Guid UserId);

/// <summary>
/// Custom FsCheck arbitraries for invalid world name generation.
/// Generates: empty strings, whitespace-only strings, and strings longer than 100 characters.
/// </summary>
public class InvalidNameArbitraries
{
    private static readonly char[] WhitespaceChars = [' ', '\t', '\n', '\r'];

    private static readonly char[] AlphanumericChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    public static Arbitrary<InvalidNameInput> InvalidNameInputs()
    {
        // Generator for empty string
        var emptyGen = Gen.Constant("");

        // Generator for whitespace-only strings (1-50 whitespace characters)
        var whitespaceOnlyGen =
            from length in Gen.Choose(1, 50)
            from chars in Gen.Elements(WhitespaceChars).ArrayOf(length)
            select new string(chars);

        // Generator for strings longer than 100 characters (101-200 chars)
        var tooLongGen =
            from length in Gen.Choose(101, 200)
            from chars in Gen.Elements(AlphanumericChars).ArrayOf(length)
            select new string(chars);

        // Combine all invalid name generators with equal probability
        var invalidNameGen = Gen.OneOf(emptyGen, whitespaceOnlyGen, tooLongGen);

        var inputGen =
            from name in invalidNameGen
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            select new InvalidNameInput(name, userId);

        return inputGen.ToArbitrary();
    }
}
