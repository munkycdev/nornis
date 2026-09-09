using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Deleting a character that has sheet snapshots attached.
///
/// A database-level test because the constraint it guards is a database fact.
/// <c>CharacterSheetSnapshot</c> reaches Worlds by two routes — through Sources and through
/// Members→Characters — and SQL Server refuses two cascade paths between the same pair of
/// tables. Source keeps the cascade, so the character side is NO ACTION and
/// <c>CharacterRepository.DeleteAsync</c> has to remove the rows itself. If it ever stops,
/// the symptom is a foreign key violation on an ordinary character delete rather than a
/// dangling row somebody notices months later.
/// </summary>
[TestFixture]
public class CharacterDeleteSnapshotCleanupTests : IntegrationTestBase
{
    private CharacterRepository _sut = null!;
    private Character _character = null!;
    private Source _source = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new CharacterRepository(Context);

        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|p-{tag}",
            Username = $"p-{tag}",
            Email = $"p-{tag}@example.com",
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

        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = user.Id,
            Role = WorldRole.Player,
            DisplayName = "Tavrin's player",
            JoinedAt = now
        };
        Context.WorldMembers.Add(member);

        _character = new Character
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            WorldMemberId = member.Id,
            Name = "Tavrin",
            CreatedAt = now,
            UpdatedAt = now
        };
        Context.Characters.Add(_character);

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = SourceType.HandwrittenNotes,
            Title = "Tavrin's sheet, session 6",
            Visibility = VisibilityScope.Private,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = user.Id
        };
        Context.Sources.Add(_source);

        Context.CharacterSheetSnapshots.Add(new CharacterSheetSnapshot
        {
            Id = Guid.NewGuid(),
            CharacterId = _character.Id,
            SourceId = _source.Id,
            AsOf = now,
            CreatedAt = now,
            CreatedByUserId = user.Id
        });

        await Context.SaveChangesAsync();
    }

    [Test]
    public async Task DeleteAsync_RemovesTheCharactersSnapshots()
    {
        await _sut.DeleteAsync(_character.Id);

        Assert.That(Context.CharacterSheetSnapshots.Any(s => s.CharacterId == _character.Id), Is.False);
        Assert.That(Context.Characters.Any(c => c.Id == _character.Id), Is.False);
    }

    [Test]
    public async Task DeleteAsync_LeavesTheSourceInTheLedger()
    {
        await _sut.DeleteAsync(_character.Id);

        Assert.That(Context.Sources.Any(s => s.Id == _source.Id), Is.True,
            "a character going away must not take the photographs of its sheet with it");
    }
}
