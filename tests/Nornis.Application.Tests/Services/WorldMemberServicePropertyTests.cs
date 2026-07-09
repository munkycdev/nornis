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
/// Property-based tests for WorldMemberService.
/// </summary>
[TestFixture]
public class WorldMemberServicePropertyTests
{
    /// <summary>
    /// Validates: Requirements 4.2, 5.2
    ///
    /// Property 7: Non-GM Operations Are Denied
    /// For any world member with role Player or Observer, attempting to update world settings,
    /// add members, remove members, or change member roles should be denied with a 403 Forbidden response.
    /// </summary>
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonGmOperationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_UpdateWorld(NonGmScenario scenario)
    {
        // Arrange
        var (worldService, _, _, world) = SetupServices(scenario);

        var command = new UpdateWorldCommand(
            world.Id,
            "Updated Name",
            "Updated Description",
            "Pathfinder 2e",
            scenario.ActingUserId);

        // Act
        var result = worldService.UpdateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Non-GM should not be able to update world settings");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403), "Should return 403 Forbidden");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonGmOperationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_AddMember(NonGmScenario scenario)
    {
        // Arrange
        var (_, memberService, userRepo, world) = SetupServices(scenario);

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
            world.Id,
            targetUserId,
            WorldRole.Player,
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
    [Description("Feature: auth-and-worlds, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_RemoveMember(NonGmScenario scenario)
    {
        // Arrange
        var (_, memberService, _, world) = SetupServices(scenario);

        // Act — try to remove the GM
        var result = memberService.RemoveMemberAsync(
            world.Id,
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
    [Description("Feature: auth-and-worlds, Property 7: Non-GM Operations Are Denied")]
    public void NonGmOperationsAreDenied_UpdateRole(NonGmScenario scenario)
    {
        // Arrange
        var (_, memberService, _, world) = SetupServices(scenario);

        var command = new UpdateMemberRoleCommand(
            world.Id,
            scenario.GmUserId,
            WorldRole.Observer,
            scenario.ActingUserId);

        // Act
        var result = memberService.UpdateRoleAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Non-GM should not be able to change member roles");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403), "Should return 403 Forbidden");
    }

    private static (WorldService, WorldMemberService, InMemoryUserRepository, World) SetupServices(NonGmScenario scenario)
    {
        var worldRepo = new InMemoryWorldRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var userRepo = new InMemoryUserRepository();

        // Create the world
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

        // Add the GM member
        memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = scenario.WorldId,
            UserId = scenario.GmUserId,
            Role = WorldRole.GM,
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
        }, CancellationToken.None).GetAwaiter().GetResult();

        // Add the non-GM acting member
        memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = scenario.WorldId,
            UserId = scenario.ActingUserId,
            Role = scenario.ActingRole,
            JoinedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None).GetAwaiter().GetResult();

        var worldService = new WorldService(worldRepo, memberRepo);
        var memberService = new WorldMemberService(memberRepo, userRepo);

        return (worldService, memberService, userRepo, world);
    }
}

/// <summary>
/// Input model for non-GM operation scenarios.
/// </summary>
public record NonGmScenario(
    Guid WorldId,
    Guid GmUserId,
    Guid ActingUserId,
    WorldRole ActingRole);

/// <summary>
/// Custom FsCheck arbitraries for non-GM operation tests.
/// </summary>
public class NonGmOperationArbitraries
{
    public static Arbitrary<NonGmScenario> NonGmScenarios()
    {
        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from actingUserId in ArbMap.Default.GeneratorFor<Guid>()
            where actingUserId != gmUserId
            from role in Gen.Elements(WorldRole.Player, WorldRole.Observer)
            select new NonGmScenario(worldId, gmUserId, actingUserId, role);

        return gen.ToArbitrary();
    }
}
