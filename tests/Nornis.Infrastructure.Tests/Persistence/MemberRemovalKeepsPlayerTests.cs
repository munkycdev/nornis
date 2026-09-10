using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Leaving a world leaves the person at the table (feature 25, B4). The membership cascade
/// used to delete the characters; now the player is unlinked, keeps every character, and
/// takes the member's name as it was — because the FK cannot SET NULL on its own (a second
/// cascade path from Worlds) and <c>WorldMemberRepository.RemoveAsync</c> does it by hand.
///
/// A database test because the constraint is a database fact: if the unlink ever stops, the
/// symptom is a foreign key violation on an ordinary member removal.
/// </summary>
[TestFixture]
public class MemberRemovalKeepsPlayerTests : IntegrationTestBase
{
    private WorldMemberRepository _sut = null!;
    private WorldMember _member = null!;
    private Player _player = null!;
    private Character _character = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new WorldMemberRepository(Context);

        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|h-{tag}",
            Username = $"henry-{tag}",
            Email = $"h-{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        Context.Users.Add(user);

        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = user.Id,
            RowVersion = []
        };
        Context.Worlds.Add(world);

        _member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = user.Id,
            Role = WorldRole.Player,
            DisplayName = "Henry",
            JoinedAt = now
        };
        Context.WorldMembers.Add(_member);

        _player = new Player
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            WorldMemberId = _member.Id,
            Name = "stale name from join time",
            CreatedAt = now,
            UpdatedAt = now
        };
        Context.Players.Add(_player);

        _character = new Character
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            PlayerId = _player.Id,
            Name = "Malliano",
            Sheet = "Rapier, lute, a grudge.",
            CreatedAt = now,
            UpdatedAt = now
        };
        Context.Characters.Add(_character);

        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Test]
    public async Task RemoveAsync_LeavesThePlayerAtTheTable_WithEveryCharacter()
    {
        var member = await Context.WorldMembers.AsNoTracking().SingleAsync(m => m.Id == _member.Id);

        await _sut.RemoveAsync(member);

        var player = await Context.Players.AsNoTracking().SingleOrDefaultAsync(p => p.Id == _player.Id);
        var character = await Context.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == _character.Id);
        var membershipRemains = await Context.WorldMembers.AnyAsync(m => m.Id == _member.Id);

        Assert.Multiple(() =>
        {
            Assert.That(membershipRemains, Is.False, "the membership is gone");
            Assert.That(player, Is.Not.Null, "the person stays at the table");
            Assert.That(player!.WorldMemberId, Is.Null, "as someone not on Nornis");
            Assert.That(player.Name, Is.EqualTo("Henry"), "under the name the table knew them by, not the one written at join time");
            Assert.That(character, Is.Not.Null, "with their character");
            Assert.That(character!.PlayerId, Is.EqualTo(_player.Id));
            Assert.That(character.Sheet, Is.EqualTo("Rapier, lute, a grudge."), "sheet and all");
        });
    }
}
