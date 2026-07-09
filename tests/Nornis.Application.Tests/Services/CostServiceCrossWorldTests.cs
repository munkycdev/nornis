using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Unit tests for CostService cross-world functionality (GetByWorldAsync).
///
/// Validates: Requirements 4.1, 4.2, 4.3
/// </summary>
[TestFixture]
public class CostServiceCrossWorldTests
{
    private InMemoryAiUsageRecordRepository _aiUsageRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private InMemoryWorldRepository _worldRepo = null!;
    private CostService _costService = null!;

    // User IDs
    private static readonly Guid KeldaUserId = Guid.NewGuid();

    // World IDs
    private static readonly Guid BlackHarborWorldId = Guid.NewGuid();
    private static readonly Guid SilverKeyWorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _aiUsageRepo = new InMemoryAiUsageRecordRepository();
        _memberRepo = new InMemoryWorldMemberRepository();
        _worldRepo = new InMemoryWorldRepository(_memberRepo);

        _costService = new CostService(
            _aiUsageRepo,
            _memberRepo,
            _worldRepo,
            NullLogger<CostService>.Instance);
    }

    [Test]
    public async Task GetByWorldAsync_returns_only_worlds_where_user_is_gm()
    {
        // Arrange — Kelda is GM in "Black Harbor Investigation", Player in "Silver Key Mystery"
        await SeedWorld(BlackHarborWorldId, "Black Harbor Investigation");
        await SeedWorld(SilverKeyWorldId, "Silver Key Mystery");

        await SeedMembership(BlackHarborWorldId, KeldaUserId, WorldRole.GM, "Kelda");
        await SeedMembership(SilverKeyWorldId, KeldaUserId, WorldRole.Player, "Kelda");

        // Seed usage records in both worlds
        await SeedUsageRecord(BlackHarborWorldId, KeldaUserId, inputTokens: 100, outputTokens: 50);
        await SeedUsageRecord(SilverKeyWorldId, KeldaUserId, inputTokens: 200, outputTokens: 100);

        // Act
        var result = await _costService.GetByWorldAsync(KeldaUserId, CancellationToken.None);

        // Assert — only Black Harbor (GM world) appears
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].WorldId, Is.EqualTo(BlackHarborWorldId));
        Assert.That(result.Value[0].WorldName, Is.EqualTo("Black Harbor Investigation"));
    }

    [Test]
    public async Task GetByWorldAsync_user_with_no_gm_worlds_returns_empty_list()
    {
        // Arrange — Kelda is Player in both worlds, GM in neither
        await SeedWorld(BlackHarborWorldId, "Black Harbor Investigation");
        await SeedWorld(SilverKeyWorldId, "Silver Key Mystery");

        await SeedMembership(BlackHarborWorldId, KeldaUserId, WorldRole.Player, "Kelda");
        await SeedMembership(SilverKeyWorldId, KeldaUserId, WorldRole.Observer, "Kelda");

        // Seed usage records
        await SeedUsageRecord(BlackHarborWorldId, KeldaUserId, inputTokens: 500, outputTokens: 250);
        await SeedUsageRecord(SilverKeyWorldId, KeldaUserId, inputTokens: 300, outputTokens: 150);

        // Act
        var result = await _costService.GetByWorldAsync(KeldaUserId, CancellationToken.None);

        // Assert — empty list, not an error
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task GetByWorldAsync_world_names_resolved_correctly()
    {
        // Arrange — Kelda is GM in both worlds
        await SeedWorld(BlackHarborWorldId, "Black Harbor Investigation");
        await SeedWorld(SilverKeyWorldId, "Silver Key Mystery");

        await SeedMembership(BlackHarborWorldId, KeldaUserId, WorldRole.GM, "Kelda");
        await SeedMembership(SilverKeyWorldId, KeldaUserId, WorldRole.GM, "Kelda");

        // Seed usage records in both worlds
        await SeedUsageRecord(BlackHarborWorldId, KeldaUserId, inputTokens: 100, outputTokens: 50);
        await SeedUsageRecord(SilverKeyWorldId, KeldaUserId, inputTokens: 200, outputTokens: 100);

        // Act
        var result = await _costService.GetByWorldAsync(KeldaUserId, CancellationToken.None);

        // Assert — both worlds returned with correct names
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));

        var worldNames = result.Value!.Select(c => c.WorldName).OrderBy(n => n).ToList();
        Assert.That(worldNames, Is.EqualTo(new[] { "Black Harbor Investigation", "Silver Key Mystery" }));

        // Verify IDs match the correct names
        var blackHarbor = result.Value!.First(c => c.WorldId == BlackHarborWorldId);
        Assert.That(blackHarbor.WorldName, Is.EqualTo("Black Harbor Investigation"));

        var silverKey = result.Value!.First(c => c.WorldId == SilverKeyWorldId);
        Assert.That(silverKey.WorldName, Is.EqualTo("Silver Key Mystery"));
    }

    #region Helpers

    private async Task SeedWorld(Guid worldId, string name)
    {
        await _worldRepo.CreateAsync(new World
        {
            Id = worldId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedByUserId = KeldaUserId
        });
    }

    private async Task SeedMembership(Guid worldId, Guid userId, WorldRole role, string displayName)
    {
        await _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            Role = role,
            DisplayName = displayName,
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });
    }

    private async Task SeedUsageRecord(Guid worldId, Guid userId, int inputTokens, int outputTokens)
    {
        await _aiUsageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            OperationType = AiOperationType.SourceExtraction,
            Model = "gpt-4o",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            EstimatedCostUsd = (inputTokens * 0.005m + outputTokens * 0.015m) / 1000m,
            DurationMs = 500,
            Succeeded = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
    }

    #endregion
}
