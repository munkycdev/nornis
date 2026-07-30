using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The template-master flag: a GM-set, tri-state update field (set / clear / leave alone)
/// that only groups the world in the switcher. It must never be conflated with IsDemo, and
/// marking a world as a template must not remove it from the owner's world list — a world
/// that isn't listed can't be selected, and re-exporting the template needs the normal UI.
/// </summary>
[TestFixture]
public class WorldServiceTemplateFlagTests
{
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private WorldService _sut = null!;
    private World _world = null!;
    private Guid _gmId;

    [SetUp]
    public void SetUp()
    {
        _members = new InMemoryWorldMemberRepository();
        _worlds = new InMemoryWorldRepository(_members);
        _sut = new WorldService(_worlds, _members, Options.Create(new DemoWorldOptions()));
        _gmId = Guid.NewGuid();

        _world = AddWorld("Vespergale Reach (template master)");
    }

    private World AddWorld(string name)
    {
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _worlds.CreateAsync(world).GetAwaiter().GetResult();
        _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = _gmId,
            Role = WorldRole.GM,
            JoinedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
        return world;
    }

    private UpdateWorldCommand Command(bool? isTemplate) =>
        new(_world.Id, null, null, null, _gmId, IsTemplate: isTemplate);

    [Test]
    public async Task Update_SetsTemplateFlag_WithoutTouchingIsDemo()
    {
        var result = await _sut.UpdateAsync(Command(isTemplate: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsTemplate, Is.True);
        Assert.That(result.Value.IsDemo, Is.False,
            "IsDemo means 'made from a template' — the opposite of IsTemplate.");
    }

    [Test]
    public async Task Update_ClearsTemplateFlag()
    {
        _world.IsTemplate = true;
        await _worlds.UpdateAsync(_world);

        var result = await _sut.UpdateAsync(Command(isTemplate: false), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsTemplate, Is.False);
    }

    [Test]
    public async Task Update_OmittingTheFlag_LeavesItUnchanged()
    {
        _world.IsTemplate = true;
        await _worlds.UpdateAsync(_world);

        var result = await _sut.UpdateAsync(Command(isTemplate: null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsTemplate, Is.True);
    }

    [Test]
    public async Task Update_ByNonGm_CannotSetTemplateFlag()
    {
        var playerId = Guid.NewGuid();
        await _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = playerId,
            Role = WorldRole.Player,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        var result = await _sut.UpdateAsync(
            new UpdateWorldCommand(_world.Id, null, null, null, playerId, IsTemplate: true),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_world.IsTemplate, Is.False);
    }

    [Test]
    public async Task ListForUser_StillIncludesTemplateWorlds_InNameOrder()
    {
        AddWorld("Aldenmoor");
        await _sut.UpdateAsync(Command(isTemplate: true), CancellationToken.None);

        var result = await _sut.ListForUserAsync(_gmId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(w => w.World.Name),
            Is.EqualTo(new[] { "Aldenmoor", "Vespergale Reach (template master)" }),
            "templates stay in the list — grouping is a UI concern — and order is stable");
        Assert.That(result.Value!.Single(w => w.World.IsTemplate).World.Id, Is.EqualTo(_world.Id));
    }
}
