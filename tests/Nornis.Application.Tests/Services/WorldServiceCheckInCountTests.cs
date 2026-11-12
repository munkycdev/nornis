using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>The per-world question count (feature 44): a partial update field held to 1..10.</summary>
[TestFixture]
public class WorldServiceCheckInCountTests
{
    private InMemoryWorldRepository _worlds = null!;
    private WorldService _sut = null!;
    private World _world = null!;
    private Guid _gmId;

    [SetUp]
    public void SetUp()
    {
        var members = new InMemoryWorldMemberRepository();
        _worlds = new InMemoryWorldRepository(members);
        _sut = new WorldService(_worlds, members, Options.Create(new DemoWorldOptions()));
        _gmId = Guid.NewGuid();
        _world = new World { Id = Guid.NewGuid(), Name = "W", CreatedByUserId = _gmId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _worlds.CreateAsync(_world).GetAwaiter().GetResult();
    }

    private UpdateWorldCommand Command(int? count) =>
        new(_world.Id, null, null, null, _gmId, WorldRole.GM, CheckInQuestionCount: count);

    [Test]
    public void NewWorld_AsksTheDefault() =>
        Assert.That(_world.CheckInQuestionCount, Is.EqualTo(World.DefaultCheckInQuestionCount));

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(10)]
    public async Task Update_SetsACountInRange(int count)
    {
        var result = await _sut.UpdateAsync(Command(count), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CheckInQuestionCount, Is.EqualTo(count));
    }

    [TestCase(0)]
    [TestCase(11)]
    [TestCase(-3)]
    public async Task Update_RefusesACountOutOfRange(int count)
    {
        var result = await _sut.UpdateAsync(Command(count), CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Message, Does.Contain("1 and 10"));
        Assert.That(_world.CheckInQuestionCount, Is.EqualTo(World.DefaultCheckInQuestionCount));
    }

    [Test]
    public async Task Update_WithoutTheField_LeavesTheCountAlone()
    {
        _world.CheckInQuestionCount = 8;

        var result = await _sut.UpdateAsync(Command(null), CancellationToken.None);

        Assert.That(result.Value!.CheckInQuestionCount, Is.EqualTo(8));
    }
}
