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
/// For any user who is not a WorldMember of a given world, accessing any world-scoped
/// endpoint for that world should return HTTP 403 Forbidden, regardless of whether the world exists.
///
/// **Validates: Requirements 8.1, 8.2, 8.5**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-worlds, Property 9: Non-Member Access Denied")]
public class NonMemberAccessDeniedTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberAccessArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 9: Non-Member Access Denied")]
    public void NonMember_GetWorldById_Returns403(NonMemberScenario scenario)
    {
        // Arrange
        var (worldService, _) = SetupServices(scenario);

        // Act — non-member attempts to get world details
        var result = worldService.GetByIdAsync(
            scenario.WorldId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to access world details.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access should return 403 Forbidden.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberAccessArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 9: Non-Member Access Denied")]
    public void NonMember_ListMembers_Returns403(NonMemberScenario scenario)
    {
        // Arrange
        var (_, memberService) = SetupServices(scenario);

        // Act — non-member attempts to list world members
        var result = memberService.ListMembersAsync(
            scenario.WorldId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to list world members.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access should return 403 Forbidden.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberNonExistentWorldArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 9: Non-Member Access Denied")]
    public void NonMember_GetNonExistentWorld_Returns403(NonMemberNonExistentWorldScenario scenario)
    {
        // Arrange — no world or members exist at all
        var worldRepo = new InMemoryWorldRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var userRepo = new InMemoryUserRepository();

        var worldService = new WorldService(worldRepo, memberRepo);

        // Act — non-member attempts to access a world that doesn't exist
        var result = worldService.GetByIdAsync(
            scenario.NonExistentWorldId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should still be 403, not 404 (Req 8.5)
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to access non-existent world.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access to non-existent world should return 403, not 404.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonMemberNonExistentWorldArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 9: Non-Member Access Denied")]
    public void NonMember_ListMembersNonExistentWorld_Returns403(NonMemberNonExistentWorldScenario scenario)
    {
        // Arrange — no world or members exist at all
        var memberRepo = new InMemoryWorldMemberRepository();
        var userRepo = new InMemoryUserRepository();

        var memberService = new WorldMemberService(memberRepo, userRepo);

        // Act — non-member attempts to list members of a world that doesn't exist
        var result = memberService.ListMembersAsync(
            scenario.NonExistentWorldId,
            scenario.NonMemberUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should still be 403, not 404 (Req 8.5)
        Assert.That(result.IsSuccess, Is.False,
            "Non-member should not be able to list members of non-existent world.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Non-member access to non-existent world members should return 403, not 404.");
    }

    private static (WorldService, WorldMemberService) SetupServices(NonMemberScenario scenario)
    {
        var worldRepo = new InMemoryWorldRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var userRepo = new InMemoryUserRepository();

        if (scenario.WorldExists)
        {
            // Create the world with a GM who is NOT the non-member user
            var world = new World
            {
                Id = scenario.WorldId,
                Name = "Black Harbor Investigation",
                Description = "Investigating the missing caravan",
                GameSystem = "D&D 5e",
                CreatedByUserId = scenario.GmUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            worldRepo.CreateAsync(world, CancellationToken.None).GetAwaiter().GetResult();

            // Add the GM as a member
            memberRepo.CreateAsync(new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = scenario.WorldId,
                UserId = scenario.GmUserId,
                Role = WorldRole.GM,
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
            }, CancellationToken.None).GetAwaiter().GetResult();
        }

        // The non-member user is NOT added to the world

        var worldService = new WorldService(worldRepo, memberRepo);
        var memberService = new WorldMemberService(memberRepo, userRepo);

        return (worldService, memberService);
    }
}

/// <summary>
/// Input model for non-member access scenarios where the world may or may not exist.
/// </summary>
public record NonMemberScenario(
    Guid WorldId,
    Guid GmUserId,
    Guid NonMemberUserId,
    bool WorldExists);

/// <summary>
/// Input model for non-member access to worlds that definitely don't exist.
/// </summary>
public record NonMemberNonExistentWorldScenario(
    Guid NonExistentWorldId,
    Guid NonMemberUserId);

/// <summary>
/// Custom FsCheck arbitraries for non-member access denied tests.
/// Generates random users who are NOT members of a world.
/// </summary>
public class NonMemberAccessArbitraries
{
    public static Arbitrary<NonMemberScenario> NonMemberScenarios()
    {
        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from nonMemberUserId in ArbMap.Default.GeneratorFor<Guid>()
            where nonMemberUserId != gmUserId
            from worldExists in Gen.Elements(true, false)
            select new NonMemberScenario(worldId, gmUserId, nonMemberUserId, worldExists);

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Custom FsCheck arbitraries for non-member access to non-existent worlds.
/// </summary>
public class NonMemberNonExistentWorldArbitraries
{
    public static Arbitrary<NonMemberNonExistentWorldScenario> NonMemberNonExistentWorldScenarios()
    {
        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from nonMemberUserId in ArbMap.Default.GeneratorFor<Guid>()
            select new NonMemberNonExistentWorldScenario(worldId, nonMemberUserId);

        return gen.ToArbitrary();
    }
}
