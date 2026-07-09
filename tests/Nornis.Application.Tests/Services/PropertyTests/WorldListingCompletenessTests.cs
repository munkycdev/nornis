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
/// Property 11: World Listing Completeness and Exclusivity
///
/// For any authenticated user, listing their worlds should return exactly the set of worlds
/// where a WorldMember record exists for that user — no more, no less — with each entry
/// including the user's WorldRole in that world.
///
/// **Validates: Requirements 7.1, 7.2, 7.3**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-worlds, Property 11: World Listing Completeness and Exclusivity")]
public class WorldListingCompletenessTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(WorldListingArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 11: World Listing Completeness and Exclusivity")]
    public void ListForUser_ReturnsExactlyMemberWorlds_WithCorrectRoles(WorldListingScenario scenario)
    {
        // Arrange
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository(memberRepo);
        var service = new WorldService(worldRepo, memberRepo);

        // Seed all worlds into the repository
        foreach (var world in scenario.AllWorlds)
        {
            worldRepo.CreateAsync(world, CancellationToken.None).GetAwaiter().GetResult();
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

        var returnedWorlds = result.Value!;

        // Determine expected world IDs (worlds where the target user has a membership)
        var expectedWorldIds = scenario.Memberships
            .Where(m => m.UserId == scenario.TargetUserId)
            .Select(m => m.WorldId)
            .ToHashSet();

        var returnedWorldIds = returnedWorlds.Select(c => c.World.Id).ToHashSet();

        // Assert completeness: all expected worlds are returned
        Assert.That(returnedWorldIds.IsSupersetOf(expectedWorldIds),
            Is.True,
            "All worlds where the user is a member must be returned.");

        // Assert exclusivity: no extra worlds are returned
        Assert.That(returnedWorldIds.IsSubsetOf(expectedWorldIds),
            Is.True,
            "No worlds where the user is NOT a member should be returned.");

        // Assert exact count matches
        Assert.That(returnedWorlds.Count, Is.EqualTo(expectedWorldIds.Count),
            "Returned world count must match the number of memberships.");

        // Assert each returned entry's Role matches the WorldMember role
        foreach (var dto in returnedWorlds)
        {
            var membership = scenario.Memberships.First(m =>
                m.WorldId == dto.World.Id && m.UserId == scenario.TargetUserId);

            Assert.That(dto.Role, Is.EqualTo(membership.Role),
                $"Role for world {dto.World.Id} must match the membership role.");
        }
    }
}

/// <summary>
/// Scenario for the world listing completeness property test.
/// </summary>
public record WorldListingScenario(
    Guid TargetUserId,
    IReadOnlyList<World> AllWorlds,
    IReadOnlyList<WorldMember> Memberships);

/// <summary>
/// Custom FsCheck arbitraries for world listing completeness tests.
/// </summary>
public class WorldListingArbitraries
{
    public static Arbitrary<WorldListingScenario> WorldListingScenarios()
    {
        var roleGen = Gen.Elements(WorldRole.GM, WorldRole.Player, WorldRole.Observer);

        var scenarioGen =
            from targetUserId in ArbMap.Default.GeneratorFor<Guid>()
            from otherUserIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(3)
            from totalWorldCount in Gen.Choose(1, 8)
            from worldIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(totalWorldCount)
            from memberWorldCount in Gen.Choose(0, totalWorldCount)
            from roles in roleGen.ArrayOf(memberWorldCount)
            let allWorlds = worldIds.Select(id => new World
            {
                Id = id,
                Name = $"World {id.ToString()[..8]}",
                Description = null,
                GameSystem = null,
                CreatedByUserId = otherUserIds.Length > 0 ? otherUserIds[0] : Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }).ToList()
            let memberWorlds = allWorlds.Take(memberWorldCount).ToList()
            let targetMemberships = memberWorlds.Select((c, i) => new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = c.Id,
                UserId = targetUserId,
                Role = roles[i],
                JoinedAt = DateTimeOffset.UtcNow
            }).ToList()
            // Add some other-user memberships to non-member worlds to verify exclusivity
            let otherMemberships = allWorlds.Skip(memberWorldCount)
                .SelectMany(c => otherUserIds.Take(1).Select(uid => new WorldMember
                {
                    Id = Guid.NewGuid(),
                    WorldId = c.Id,
                    UserId = uid,
                    Role = WorldRole.GM,
                    JoinedAt = DateTimeOffset.UtcNow
                })).ToList()
            let allMemberships = targetMemberships.Concat(otherMemberships).ToList()
            select new WorldListingScenario(
                targetUserId,
                allWorlds.AsReadOnly(),
                allMemberships.AsReadOnly());

        return scenarioGen.ToArbitrary();
    }
}
