using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The table's players: who may add and remove the ones not on Nornis, and what claiming does.
/// The property worth the most here is conservation — claiming and linking move characters
/// and never lose or duplicate one — so those tests compare the world's whole character set
/// before and after, not a count.
/// </summary>
[TestFixture]
public class PlayerServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryCharacterRepository _characters = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private InMemoryPlayerRepository _players = null!;
    private PlayerService _sut = null!;

    private WorldMember _gm = null!;
    private WorldMember _player = null!;
    private WorldMember _observer = null!;

    [SetUp]
    public async Task SetUp()
    {
        _characters = new InMemoryCharacterRepository();
        _members = new InMemoryWorldMemberRepository();
        _players = new InMemoryPlayerRepository(_characters);
        _sut = new PlayerService(_players, _members, _characters);

        _gm = await AddMember(WorldRole.GM, "Dave");
        _player = await AddMember(WorldRole.Player, "Sam");
        _observer = await AddMember(WorldRole.Observer, null);
    }

    private async Task<WorldMember> AddMember(WorldRole role, string? displayName)
    {
        var member = WorldMembership.Create(WorldId, Guid.NewGuid(), role, DateTimeOffset.UtcNow, displayName);
        await _members.CreateAsync(member);
        await _players.CreateAsync(member.Player!);
        return member;
    }

    private async Task<Player> AddHenry()
    {
        var result = await _sut.CreateAsync(WorldId, "Henry", WorldRole.GM, CancellationToken.None);
        return result.Value!;
    }

    private Character SeedCharacter(Player player, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var character = new Character { Id = Guid.NewGuid(), WorldId = WorldId, PlayerId = player.Id, Name = name, CreatedAt = now, UpdatedAt = now };
        _characters.Seed(character);
        return character;
    }

    private HashSet<Guid> WorldCharacterIds() => _characters.Characters.Where(c => c.WorldId == WorldId).Select(c => c.Id).ToHashSet();

    // ------------------------------------------------------------------- Listing --

    [Test]
    public async Task ListByWorldAsync_NamesMembersByTheirMembershipAndOthersByTheirName()
    {
        await AddHenry();

        var list = await _sut.ListByWorldAsync(WorldId, CancellationToken.None);

        var byName = list.Value!.ToDictionary(p => p.Name);
        Assert.Multiple(() =>
        {
            Assert.That(byName["Dave"].Role, Is.EqualTo(WorldRole.GM));
            Assert.That(byName["Sam"].WorldMemberId, Is.EqualTo(_player.Id));
            Assert.That(byName["Henry"].WorldMemberId, Is.Null, "not on Nornis");
            Assert.That(byName["Henry"].Role, Is.Null);
            Assert.That(byName.Keys.Any(k => k.StartsWith("User ")), Is.True, "a member with no display name reads by the public-safe fallback");
            Assert.That(list.Value![0].Name, Is.EqualTo("Dave"), "GM first, the way the table sits");
        });
    }

    // ------------------------------------------------------------ Add / rename --

    [Test]
    public async Task CreateAsync_OnlyAGmAddsToTheTable()
    {
        var asPlayer = await _sut.CreateAsync(WorldId, "Henry", WorldRole.Player, CancellationToken.None);
        var asGm = await _sut.CreateAsync(WorldId, "Henry", WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(asPlayer.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(asGm.IsSuccess, Is.True);
            Assert.That(asGm.Value!.WorldMemberId, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateAsync_NeedsAName(string name)
    {
        var result = await _sut.CreateAsync(WorldId, name, WorldRole.GM, CancellationToken.None);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task RenameAsync_ALinkedPlayerIsNamedByTheMember()
    {
        var result = await _sut.RenameAsync(_player.Player!.Id, WorldId, "Samuel", WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("player_linked"));
    }

    [Test]
    public async Task RenameAsync_AnUnlinkedPlayerTakesTheNewName()
    {
        var henry = await AddHenry();

        var result = await _sut.RenameAsync(henry.Id, WorldId, "Henri", WorldRole.GM, CancellationToken.None);
        var list = await _sut.ListByWorldAsync(WorldId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(list.Value!.Select(p => p.Name), Does.Contain("Henri"));
        });
    }

    // ------------------------------------------------------------------ Delete --

    [Test]
    public async Task DeleteAsync_RefusesWhileCharactersRemain_NamingThem()
    {
        var henry = await AddHenry();
        SeedCharacter(henry, "Malliano");

        var result = await _sut.DeleteAsync(henry.Id, WorldId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("player_has_characters"));
            Assert.That(result.Error.Message, Does.Contain("Malliano"));
            Assert.That(_players.Players.Any(p => p.Id == henry.Id), Is.True, "still at the table");
        });
    }

    [Test]
    public async Task DeleteAsync_AnEmptyUnlinkedPlayerGoes()
    {
        var henry = await AddHenry();

        var result = await _sut.DeleteAsync(henry.Id, WorldId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_players.Players.Any(p => p.Id == henry.Id), Is.False);
        });
    }

    [Test]
    public async Task DeleteAsync_AMembersPlayerGoesWithTheMember_NotHere()
    {
        var result = await _sut.DeleteAsync(_player.Player!.Id, WorldId, WorldRole.GM, CancellationToken.None);
        Assert.That(result.Error!.Code, Is.EqualTo("player_linked"));
    }

    // ------------------------------------------------------------------- Claim --

    [Test]
    public async Task ClaimAsync_MovesEveryCharacterAndDropsTheName_ConservingTheSet()
    {
        var henry = await AddHenry();
        var malliano = SeedCharacter(henry, "Malliano");
        var second = SeedCharacter(henry, "Malliano's mule");
        var tavrin = SeedCharacter(_player.Player!, "Tavrin");
        var before = WorldCharacterIds();

        var result = await _sut.ClaimAsync(henry.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Id, Is.EqualTo(_player.Player!.Id), "answers with the claimant's own player");
            Assert.That(WorldCharacterIds(), Is.EquivalentTo(before), "no character lost or duplicated");
            Assert.That(_characters.Characters.Single(c => c.Id == malliano.Id).PlayerId, Is.EqualTo(_player.Player!.Id));
            Assert.That(_characters.Characters.Single(c => c.Id == second.Id).PlayerId, Is.EqualTo(_player.Player!.Id));
            Assert.That(_characters.Characters.Single(c => c.Id == tavrin.Id).PlayerId, Is.EqualTo(_player.Player!.Id), "what was already theirs stays theirs");
            Assert.That(_players.Players.Any(p => p.Id == henry.Id), Is.False, "the unlinked player is gone");
            Assert.That(_players.Players.Count(p => p.WorldMemberId == _player.Id), Is.EqualTo(1), "still one player per member");
        });
    }

    [Test]
    public async Task ClaimAsync_ALinkedPlayer_IsRefused()
    {
        var result = await _sut.ClaimAsync(_gm.Player!.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        Assert.That(result.Error!.Code, Is.EqualTo("player_linked"));
    }

    [Test]
    public async Task ClaimAsync_YourOwnPlayer_IsANoOp()
    {
        var result = await _sut.ClaimAsync(_player.Player!.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Id, Is.EqualTo(_player.Player!.Id));
        });
    }

    [Test]
    public async Task ClaimAsync_Observer_Returns403()
    {
        var henry = await AddHenry();
        var result = await _sut.ClaimAsync(henry.Id, WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task ClaimAsync_AnotherWorldsPlayer_Is404()
    {
        var elsewhere = await _players.CreateAsync(new Player { Id = Guid.NewGuid(), WorldId = Guid.NewGuid(), Name = "Henry", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        var result = await _sut.ClaimAsync(elsewhere.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // -------------------------------------------------------------------- Link --

    [Test]
    public async Task LinkAsync_IsClaimingDoneByTheGm()
    {
        var henry = await AddHenry();
        var malliano = SeedCharacter(henry, "Malliano");
        var before = WorldCharacterIds();

        var asPlayer = await _sut.LinkAsync(henry.Id, WorldId, _player.Id, WorldRole.Player, CancellationToken.None);
        var asGm = await _sut.LinkAsync(henry.Id, WorldId, _player.Id, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(asPlayer.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(asGm.IsSuccess, Is.True);
            Assert.That(asGm.Value!.Id, Is.EqualTo(_player.Player!.Id));
            Assert.That(WorldCharacterIds(), Is.EquivalentTo(before));
            Assert.That(_characters.Characters.Single(c => c.Id == malliano.Id).PlayerId, Is.EqualTo(_player.Player!.Id));
            Assert.That(_players.Players.Any(p => p.Id == henry.Id), Is.False);
        });
    }

    [Test]
    public async Task LinkAsync_ToSomeoneOutsideTheWorld_Is400()
    {
        var henry = await AddHenry();
        var result = await _sut.LinkAsync(henry.Id, WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    // ------------------------------------------------------------------- Names --

    [Test]
    public void PlayerDisplayName_NeverSaysTheUsername()
    {
        var member = WorldMembership.Create(WorldId, Guid.NewGuid(), WorldRole.Player, DateTimeOffset.UtcNow);
        var unlinked = new Player { Id = Guid.NewGuid(), WorldId = WorldId, Name = "Henry" };

        Assert.Multiple(() =>
        {
            Assert.That(PlayerDisplayName.For(member.Player!, member), Does.StartWith("User "), "no display name: the public-safe fallback");
            Assert.That(PlayerDisplayName.For(member.Player!, member), Has.Length.EqualTo("User ".Length + 8));
            Assert.That(PlayerDisplayName.For(unlinked, (WorldMember?)null), Is.EqualTo("Henry"));
        });

        member.DisplayName = "Sam";
        Assert.That(PlayerDisplayName.For(member.Player!, member), Is.EqualTo("Sam"), "the membership is the name's home while linked");
    }
}
