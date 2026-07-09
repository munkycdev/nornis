using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Unit tests for CostService role-based filtering behavior.
/// Verifies that GM sees all users' records, while Player and Observer
/// see only their own data.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
[TestFixture]
public class CostServiceRoleFilteringTests
{
    private InMemoryAiUsageRecordRepository _aiUsageRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private InMemoryWorldRepository _worldRepo = null!;
    private CostService _costService = null!;

    private Guid _worldId;
    private Guid _keldaId;   // GM
    private Guid _tavrinId;  // Player
    private Guid _jorinId;   // Observer

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

        _worldId = Guid.NewGuid();
        _keldaId = Guid.NewGuid();
        _tavrinId = Guid.NewGuid();
        _jorinId = Guid.NewGuid();

        // Set up world members: Kelda (GM), Tavrin (Player), Jorin (Observer)
        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = _keldaId,
            Role = WorldRole.GM,
            DisplayName = "Kelda",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-30)
        }).GetAwaiter().GetResult();

        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = _tavrinId,
            Role = WorldRole.Player,
            DisplayName = "Tavrin",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-14)
        }).GetAwaiter().GetResult();

        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = _jorinId,
            Role = WorldRole.Observer,
            DisplayName = "Jorin",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
        }).GetAwaiter().GetResult();

        // Seed usage records: 3 for Kelda, 2 for Tavrin, 1 for Jorin
        SeedRecord(_keldaId, AiOperationType.SourceExtraction, "gpt-4o", 500, 200);
        SeedRecord(_keldaId, AiOperationType.AskLoremaster, "gpt-4o", 1000, 800);
        SeedRecord(_keldaId, AiOperationType.ArtifactSummary, "gpt-4o-mini", 300, 150);
        SeedRecord(_tavrinId, AiOperationType.AskLoremaster, "gpt-4o", 400, 350);
        SeedRecord(_tavrinId, AiOperationType.AskLoremaster, "gpt-4o-mini", 250, 100);
        SeedRecord(_jorinId, AiOperationType.AskLoremaster, "gpt-4o", 200, 150);
    }

    [Test]
    public async Task GetSummaryAsync_AsGm_ReturnsAllUsersRecords()
    {
        // Act
        var result = await _costService.GetSummaryAsync(
            _worldId, _keldaId, WorldRole.GM, CancellationToken.None);

        // Assert — GM sees all 6 records
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AllTime.OperationCount, Is.EqualTo(6));
    }

    [Test]
    public async Task GetSummaryAsync_AsPlayer_ReturnsOnlyOwnRecords()
    {
        // Act
        var result = await _costService.GetSummaryAsync(
            _worldId, _tavrinId, WorldRole.Player, CancellationToken.None);

        // Assert — Tavrin sees only their 2 records
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AllTime.OperationCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetSummaryAsync_AsObserver_ReturnsOnlyOwnRecords()
    {
        // Act
        var result = await _costService.GetSummaryAsync(
            _worldId, _jorinId, WorldRole.Observer, CancellationToken.None);

        // Assert — Jorin sees only their 1 record
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AllTime.OperationCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetByUserAsync_AsPlayer_ReturnsOnlyOwnEntry()
    {
        // Act
        var result = await _costService.GetByUserAsync(
            _worldId, _tavrinId, WorldRole.Player, null, null, CancellationToken.None);

        // Assert — Player gets a single-element list with only their own summary
        Assert.That(result.IsSuccess, Is.True);
        var users = result.Value!;
        Assert.That(users, Has.Count.EqualTo(1));
        Assert.That(users[0].UserId, Is.EqualTo(_tavrinId));
        Assert.That(users[0].Username, Is.EqualTo("Tavrin"));
        Assert.That(users[0].Summary.OperationCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByUserAsync_AsGm_ReturnsAllUsersEntries()
    {
        // Act
        var result = await _costService.GetByUserAsync(
            _worldId, _keldaId, WorldRole.GM, null, null, CancellationToken.None);

        // Assert — GM sees entries for all 3 users
        Assert.That(result.IsSuccess, Is.True);
        var users = result.Value!;
        Assert.That(users, Has.Count.EqualTo(3));
        var totalOps = users.Sum(u => u.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(6));
    }

    [Test]
    public async Task GetByUserAsync_AsObserver_ReturnsOnlyOwnEntry()
    {
        // Act
        var result = await _costService.GetByUserAsync(
            _worldId, _jorinId, WorldRole.Observer, null, null, CancellationToken.None);

        // Assert — Observer gets a single-element list with only their own summary
        Assert.That(result.IsSuccess, Is.True);
        var users = result.Value!;
        Assert.That(users, Has.Count.EqualTo(1));
        Assert.That(users[0].UserId, Is.EqualTo(_jorinId));
        Assert.That(users[0].Username, Is.EqualTo("Jorin"));
        Assert.That(users[0].Summary.OperationCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetByOperationTypeAsync_AsGm_ReturnsAllRecordsAcrossTypes()
    {
        // Act
        var result = await _costService.GetByOperationTypeAsync(
            _worldId, _keldaId, WorldRole.GM, null, null, CancellationToken.None);

        // Assert — total across groups equals all 6 records
        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(6));
    }

    [Test]
    public async Task GetByOperationTypeAsync_AsPlayer_ReturnsOnlyOwnRecords()
    {
        // Act
        var result = await _costService.GetByOperationTypeAsync(
            _worldId, _tavrinId, WorldRole.Player, null, null, CancellationToken.None);

        // Assert — Tavrin sees only their 2 AskLoremaster records
        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByModelAsync_AsObserver_ReturnsOnlyOwnRecords()
    {
        // Act
        var result = await _costService.GetByModelAsync(
            _worldId, _jorinId, WorldRole.Observer, null, null, CancellationToken.None);

        // Assert — Jorin sees only their 1 record
        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(1));
    }

    private void SeedRecord(
        Guid userId,
        AiOperationType operationType,
        string model,
        int inputTokens,
        int outputTokens)
    {
        _aiUsageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = userId,
            OperationType = operationType,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            EstimatedCostUsd = (inputTokens * 0.005m + outputTokens * 0.015m) / 1000m,
            DurationMs = 350,
            Succeeded = true,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        }).GetAwaiter().GetResult();
    }
}
