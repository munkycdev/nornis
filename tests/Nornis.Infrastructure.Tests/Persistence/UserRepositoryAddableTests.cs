using NUnit.Framework;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// <see cref="UserRepository.ListAddableToWorldAsync"/> replaced a plain "every user" listing that
/// the add-member picker downloaded whole. That endpoint handed the entire user directory — id and
/// username for every account — to any authenticated caller, and the picker then filtered out
/// existing members in the browser.
///
/// <para>The exclusion and the cap are now the query's job, so they are tested against a real
/// provider rather than a fake: a hand-written in-memory stand-in would be asserting my own
/// re-implementation of the join, which is the part that could be wrong.</para>
/// </summary>
[TestFixture]
public class UserRepositoryAddableTests : IntegrationTestBase
{
    private const int NoCap = 1000;

    /// <summary>
    /// NUnit reuses one fixture instance for the whole class, so <see cref="IntegrationTestBase"/>
    /// builds the SQLite database once and every test in here shares it. Tests that assert on an
    /// exact result set therefore tag their usernames, so they are matching their own rows rather
    /// than whatever an earlier test happened to leave behind.
    /// </summary>
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private Guid SeedUser(string username)
    {
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = username,
            Email = $"{tag}@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        Context.Users.Add(user);
        return user.Id;
    }

    private Guid SeedWorld()
    {
        var now = DateTimeOffset.UtcNow;
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = $"World {Guid.NewGuid():N}",
            CreatedByUserId = SeedUser($"owner-{Guid.NewGuid():N}"),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Context.Worlds.Add(world);
        return world.Id;
    }

    private void SeedMembership(Guid worldId, Guid userId, WorldRole role = WorldRole.Player) =>
        Context.WorldMembers.Add(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        });

    private UserRepository Repository => new(Context);

    [Test]
    public async Task ExistingMembersAreExcluded()
    {
        var worldId = SeedWorld();
        var member = SeedUser("already-here");
        var outsider = SeedUser("not-yet");
        SeedMembership(worldId, member);
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, search: null, NoCap);

        Assert.Multiple(() =>
        {
            Assert.That(addable.Select(u => u.Id), Does.Contain(outsider));
            Assert.That(addable.Select(u => u.Id), Does.Not.Contain(member),
                "offering someone who is already a member is the one thing the picker must not do");
        });
    }

    [Test]
    public async Task MembershipOfAnotherWorldDoesNotExclude()
    {
        // The exclusion is per world. Someone in one campaign is a perfectly good candidate for
        // another, and a join that ignored the world id would hide them from every picker.
        var worldId = SeedWorld();
        var elsewhere = SeedWorld();
        var user = SeedUser("plays-elsewhere");
        SeedMembership(elsewhere, user);
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, search: null, NoCap);

        Assert.That(addable.Select(u => u.Id), Does.Contain(user));
    }

    [Test]
    public async Task SearchMatchesAnywhereInTheUsername()
    {
        // Usernames here are Discord handles, and a GM adding someone usually recalls a fragment
        // rather than the opening characters — StartsWith would fail the common case.
        var tag = Tag();
        var worldId = SeedWorld();
        var target = SeedUser($"thornwood_{tag}");
        SeedUser($"unrelated_{tag}");
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, $"wood_{tag}", NoCap);

        Assert.That(addable.Select(u => u.Id), Is.EqualTo(new[] { target }));
    }

    [Test]
    public async Task SearchDoesNotReadmitExistingMembers()
    {
        // Both filters have to survive together. A search that re-widened past the membership
        // exclusion would put an existing member back in the picker.
        var tag = Tag();
        var worldId = SeedWorld();
        var member = SeedUser($"thornwood_{tag}");
        SeedMembership(worldId, member);
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, $"thornwood_{tag}", NoCap);

        Assert.That(addable, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task BlankSearchListsEveryoneAddable(string? search)
    {
        // An empty box is the picker's opening state, not a request for nothing.
        var tag = Tag();
        var worldId = SeedWorld();
        SeedUser($"alice-{tag}");
        SeedUser($"bob-{tag}");
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, search, NoCap);

        Assert.That(addable.Select(u => u.Username), Is.SupersetOf(new[] { $"alice-{tag}", $"bob-{tag}" }));
    }

    [Test]
    public async Task TheLimitIsApplied()
    {
        // This is the whole point of the cap: the response size stops tracking the user table, so
        // the directory cannot be pulled in one call as the product grows.
        var tag = Tag();
        var worldId = SeedWorld();
        for (var i = 0; i < 10; i++)
        {
            SeedUser($"user-{tag}-{i:00}");
        }
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, $"user-{tag}", limit: 3);

        Assert.That(addable, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ResultsAreOrderedByUsernameSoTheCapIsStable()
    {
        // With a cap, ordering decides WHICH rows are returned. Without an explicit OrderBy the
        // page is whatever the server happens to hand back, and two identical requests can show
        // different people.
        // Tagged like the rest: asserting Is.Ordered over the WHOLE addable set would drag in
        // every user other tests in this fixture left in the shared database, and would start
        // failing the day one of them seeds a capitalised name — SQLite orders BINARY while
        // NUnit compares by culture, and the two only agree on lowercase ASCII.
        var tag = Tag();
        var worldId = SeedWorld();
        SeedUser($"{tag}-zed");
        SeedUser($"{tag}-adam");
        SeedUser($"{tag}-mabel");
        await Context.SaveChangesAsync();

        var addable = await Repository.ListAddableToWorldAsync(worldId, tag, NoCap);
        var names = addable.Select(u => u.Username).ToList();

        Assert.That(names, Is.EqualTo(new[] { $"{tag}-adam", $"{tag}-mabel", $"{tag}-zed" }));
    }
}
