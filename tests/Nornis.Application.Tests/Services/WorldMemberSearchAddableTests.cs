using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The add-member picker's candidate search, tested at the service rather than through the API.
///
/// <para>The controller repeats both of these checks, so an endpoint test passes even with the
/// service's own guards removed — which is exactly why they need covering here. This is the only
/// query in the application that reads across the user table instead of within a world, and the
/// next caller of it may not be a controller. If the rules live only in the HTTP layer, the first
/// import flow or admin surface that calls the service directly serves the whole directory.</para>
/// </summary>
[TestFixture]
public class WorldMemberSearchAddableTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryWorldMemberRepository _memberRepository = null!;
    private InMemoryUserRepository _userRepository = null!;
    private WorldMemberService _sut = null!;

    private Guid _gmId;
    private Guid _playerId;

    [SetUp]
    public async Task SetUp()
    {
        _memberRepository = new InMemoryWorldMemberRepository();
        _userRepository = new InMemoryUserRepository();
        _sut = new WorldMemberService(_memberRepository, _userRepository);

        _gmId = await SeedMember(WorldRole.GM, "captain_voss");
        _playerId = await SeedMember(WorldRole.Player, "tavrin_ash");
        SeedUser("mira_kell");
        SeedUser("silverfang_dm");
    }

    private Guid SeedUser(string username)
    {
        var id = Guid.NewGuid();
        _userRepository.CreateAsync(new User
        {
            Id = id,
            Auth0SubjectId = $"auth0|{id:N}",
            Username = username,
            Email = $"{id:N}@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
        return id;
    }

    private async Task<Guid> SeedMember(WorldRole role, string username)
    {
        var id = SeedUser(username);
        await _memberRepository.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = id,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        _userRepository.SeedMembership(WorldId, id);
        return id;
    }

    private Task<Nornis.Application.Errors.AppResult<IReadOnlyList<User>>> Search(Guid actingUserId, string? term) =>
        _sut.SearchAddableUsersAsync(WorldId, actingUserId, term, CancellationToken.None);

    // ------------------------------------------------------------------ who may ask

    [Test]
    public async Task APlayerIsRefused()
    {
        var result = await Search(_playerId, "mira");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task ANonMemberIsRefused()
    {
        var result = await Search(Guid.NewGuid(), "mira");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task AGmIsAllowed()
    {
        var result = await Search(_gmId, "mira");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(u => u.Username), Is.EqualTo(new[] { "mira_kell" }));
    }

    // ------------------------------------------------------------------ what may be asked

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("m")]
    [TestCase(" m ")]
    public async Task AMissingOrTooShortTermIsRefused(string? term)
    {
        // There is deliberately no "everything" mode. A GM check cannot protect the directory on
        // its own — anyone can create a world and be its GM — so what keeps the user table from
        // being paged out is that every call has to name someone.
        var result = await Search(_gmId, term);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(result.Error.Code, Is.EqualTo("search_too_short"));
        });
    }

    [Test]
    public async Task TheRoleIsCheckedBeforeTheTerm()
    {
        // A Player sending a blank term must learn that they may not search, not that their term
        // was short — otherwise the error itself confirms the endpoint exists and is worth probing.
        var result = await Search(_playerId, "");

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    // ------------------------------------------------------------------ what comes back

    [Test]
    public async Task ExistingMembersAreNotOffered()
    {
        var result = await Search(_gmId, "tavrin");

        Assert.That(result.Value!, Is.Empty, "Tavrin is already a Player in this world");
    }

    [Test]
    public async Task TheTermIsTrimmedBeforeMatching()
    {
        var result = await Search(_gmId, "  mira  ");

        Assert.That(result.Value!.Select(u => u.Username), Is.EqualTo(new[] { "mira_kell" }));
    }

    [Test]
    public async Task TheCapIsTheServicesToApply()
    {
        // The limit is decided here rather than by the caller, so no caller can opt out of it.
        for (var i = 0; i < WorldMemberService.MaxAddableResults + 10; i++)
        {
            SeedUser($"crowd_{i:000}");
        }

        var result = await Search(_gmId, "crowd_");

        Assert.That(result.Value!, Has.Count.EqualTo(WorldMemberService.MaxAddableResults));
    }
}
