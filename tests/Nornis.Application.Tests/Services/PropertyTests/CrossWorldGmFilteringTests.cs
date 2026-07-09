using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 6: Cross-World View Shows Only GM Worlds
///
/// For any user who is a member of multiple worlds with varying roles,
/// the by-world breakdown SHALL include only those worlds where the user holds
/// the GM role. Worlds where the user is a Player or Observer SHALL not appear.
///
/// **Validates: Requirements 4.2**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 6: Cross-world GM filtering")]
public class CrossWorldGmFilteringTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CrossWorldScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 6: Cross-world GM filtering")]
    public void GetByWorldAsync_ReturnsOnlyGmWorlds(CrossWorldScenario scenario)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();

        // Seed worlds
        foreach (var world in scenario.Worlds)
        {
            worldRepo.CreateAsync(world, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed memberships
        foreach (var membership in scenario.Memberships)
        {
            memberRepo.CreateAsync(membership, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed AI usage records for all worlds
        foreach (var record in scenario.UsageRecords)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        var costService = new CostService(
            aiUsageRepo,
            memberRepo,
            worldRepo,
            NullLogger<CostService>.Instance);

        // Act
        var result = costService.GetByWorldAsync(scenario.UserId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByWorldAsync should succeed.");

        var worldResults = result.Value!;
        var returnedWorldIds = worldResults.Select(c => c.WorldId).ToHashSet();

        // Only GM worlds should appear in results
        var gmWorldIds = scenario.Memberships
            .Where(m => m.UserId == scenario.UserId && m.Role == WorldRole.GM)
            .Select(m => m.WorldId)
            .ToHashSet();

        // GM worlds with usage records should be present
        var gmWorldsWithRecords = gmWorldIds
            .Where(id => scenario.UsageRecords.Any(r => r.WorldId == id))
            .ToHashSet();

        Assert.That(returnedWorldIds, Is.EquivalentTo(gmWorldsWithRecords),
            "Results should contain exactly the GM worlds that have usage records.");

        // No Player or Observer worlds should appear
        var nonGmWorldIds = scenario.Memberships
            .Where(m => m.UserId == scenario.UserId && m.Role != WorldRole.GM)
            .Select(m => m.WorldId)
            .ToHashSet();

        Assert.That(returnedWorldIds.Intersect(nonGmWorldIds), Is.Empty,
            "No Player or Observer worlds should appear in results.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CrossWorldScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 6: Cross-world GM filtering")]
    public void GetByWorldAsync_ResolvesCorrectWorldNames(CrossWorldScenario scenario)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();

        foreach (var world in scenario.Worlds)
        {
            worldRepo.CreateAsync(world, CancellationToken.None).GetAwaiter().GetResult();
        }

        foreach (var membership in scenario.Memberships)
        {
            memberRepo.CreateAsync(membership, CancellationToken.None).GetAwaiter().GetResult();
        }

        foreach (var record in scenario.UsageRecords)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        var costService = new CostService(
            aiUsageRepo,
            memberRepo,
            worldRepo,
            NullLogger<CostService>.Instance);

        // Act
        var result = costService.GetByWorldAsync(scenario.UserId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        var worldNameMap = scenario.Worlds.ToDictionary(c => c.Id, c => c.Name);

        foreach (var worldResult in result.Value!)
        {
            Assert.That(worldNameMap.ContainsKey(worldResult.WorldId), Is.True,
                "Returned world ID should exist in seeded worlds.");
            Assert.That(worldResult.WorldName, Is.EqualTo(worldNameMap[worldResult.WorldId]),
                "World name should match the seeded world name.");
        }
    }
}

/// <summary>
/// Input model for cross-world GM filtering property tests.
/// </summary>
public record CrossWorldScenario(
    Guid UserId,
    List<World> Worlds,
    List<WorldMember> Memberships,
    List<AiUsageRecord> UsageRecords);

/// <summary>
/// Custom FsCheck arbitraries for cross-world GM filtering property tests.
/// Generates a user with memberships in multiple worlds with mixed roles (GM, Player, Observer).
/// Ensures at least one GM world and at least one non-GM world exist.
/// </summary>
public class CrossWorldScenarioArbitraries
{
    private static readonly string[] WorldNames =
    [
        "Black Harbor Investigation",
        "Silver Key Mystery",
        "Missing Caravan",
        "Captain Voss Pursuit",
        "The Sunken Temple",
        "Ruins of Aldermoor",
        "The Crimson Accord"
    ];

    private static readonly AiOperationType[] OperationTypes =
    [
        AiOperationType.SourceExtraction,
        AiOperationType.ArtifactSummary,
        AiOperationType.AskLoremaster,
        AiOperationType.SourceExtractionRepair
    ];

    private static readonly string[] Models =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-3.5-turbo"
    ];

    public static Arbitrary<CrossWorldScenario> CrossWorldScenarios()
    {
        var gen =
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from gmCount in Gen.Choose(1, 3)
            from playerCount in Gen.Choose(1, 2)
            from observerCount in Gen.Choose(0, 2)
            let totalWorlds = gmCount + playerCount + observerCount
            from worldIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(totalWorlds)
            let distinctWorldIds = worldIds.Distinct().ToArray()
            where distinctWorldIds.Length >= gmCount + playerCount + observerCount
            from nameIndices in Gen.Elements(
                Enumerable.Range(0, WorldNames.Length).ToArray())
                .ArrayOf(distinctWorldIds.Length)
            let worlds = distinctWorldIds
                .Select((id, i) => new World
                {
                    Id = id,
                    Name = WorldNames[nameIndices[i] % WorldNames.Length],
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    CreatedByUserId = userId
                }).ToList()
            let memberships = BuildMemberships(userId, distinctWorldIds, gmCount, playerCount, observerCount)
            from recordsPerWorld in Gen.Choose(1, 5)
            from records in GenUsageRecords(userId, distinctWorldIds.ToList(), recordsPerWorld)
            select new CrossWorldScenario(userId, worlds, memberships, records);

        return gen.ToArbitrary();
    }

    private static List<WorldMember> BuildMemberships(
        Guid userId, Guid[] worldIds, int gmCount, int playerCount, int observerCount)
    {
        var memberships = new List<WorldMember>();
        var index = 0;

        // Assign GM roles first
        for (var i = 0; i < gmCount && index < worldIds.Length; i++, index++)
        {
            memberships.Add(new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = worldIds[index],
                UserId = userId,
                Role = WorldRole.GM,
                DisplayName = "Kelda",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-14)
            });
        }

        // Assign Player roles
        for (var i = 0; i < playerCount && index < worldIds.Length; i++, index++)
        {
            memberships.Add(new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = worldIds[index],
                UserId = userId,
                Role = WorldRole.Player,
                DisplayName = "Tavrin",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-10)
            });
        }

        // Assign Observer roles
        for (var i = 0; i < observerCount && index < worldIds.Length; i++, index++)
        {
            memberships.Add(new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = worldIds[index],
                UserId = userId,
                Role = WorldRole.Observer,
                DisplayName = "Jorin",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-5)
            });
        }

        return memberships;
    }

    private static Gen<List<AiUsageRecord>> GenUsageRecords(
        Guid userId, List<Guid> worldIds, int recordsPerWorld)
    {
        var generators = worldIds
            .SelectMany(worldId =>
                Enumerable.Range(0, recordsPerWorld)
                    .Select(_ => GenSingleRecord(userId, worldId)));

        return generators.Aggregate(
            Gen.Constant(new List<AiUsageRecord>()),
            (accGen, recordGen) =>
                from acc in accGen
                from record in recordGen
                select new List<AiUsageRecord>(acc) { record });
    }

    private static Gen<AiUsageRecord> GenSingleRecord(Guid userId, Guid worldId)
    {
        return
            from operationType in Gen.Elements(OperationTypes)
            from model in Gen.Elements(Models)
            from inputTokens in Gen.Choose(10, 5000)
            from outputTokens in Gen.Choose(10, 3000)
            from costCents in Gen.Choose(1, 500)
            select new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = userId,
                OperationType = operationType,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                EstimatedCostUsd = costCents / 100m,
                DurationMs = 150,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
    }
}
