using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The import session's persistence contract: one non-terminal session per world is what the
/// service's 409 rests on, items always come back with the session, and the scoped writes
/// (status, positions, skip) run as real SQL rather than whole-graph saves.
/// </summary>
[TestFixture]
public class ImportSessionRepositoryTests : IntegrationTestBase
{
    private Guid _worldId;
    private Guid _gmId;
    private ImportSessionRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();

        Context.ImportSessionItems.RemoveRange(Context.ImportSessionItems);
        Context.ImportSessions.RemoveRange(Context.ImportSessions);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.Add(new User
        {
            Id = _gmId,
            Auth0SubjectId = $"auth0|{_gmId:N}",
            Username = "kelda",
            Email = "kelda@example.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        });
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor Investigation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _gmId,
            RowVersion = []
        });
        await Context.SaveChangesAsync();

        _repository = new ImportSessionRepository(Context);
    }

    [Test]
    public async Task GetNonTerminal_FindsDraftAndInProgressOnly()
    {
        var draft = await SeedSessionAsync(ImportSessionStatus.Draft);

        var found = await _repository.GetNonTerminalByWorldAsync(_worldId);
        Assert.That(found!.Id, Is.EqualTo(draft.Id));

        await _repository.UpdateAsync(draft.Id, ImportSessionStatus.InProgress, DateTimeOffset.UtcNow);
        Assert.That((await _repository.GetNonTerminalByWorldAsync(_worldId))!.Id, Is.EqualTo(draft.Id));

        await _repository.UpdateAsync(draft.Id, ImportSessionStatus.Completed, DateTimeOffset.UtcNow);
        Assert.That(await _repository.GetNonTerminalByWorldAsync(_worldId), Is.Null);

        await _repository.UpdateAsync(draft.Id, ImportSessionStatus.Abandoned, DateTimeOffset.UtcNow);
        Assert.That(await _repository.GetNonTerminalByWorldAsync(_worldId), Is.Null);
    }

    [Test]
    public async Task GetById_IncludesItems()
    {
        var session = await SeedSessionAsync(ImportSessionStatus.Draft);
        await SeedItemAsync(session.Id, position: 0);
        await SeedItemAsync(session.Id, position: 1);

        var loaded = await _repository.GetByIdAsync(session.Id);

        Assert.That(loaded!.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SetItemPositions_RewritesTheWalkOrder()
    {
        var session = await SeedSessionAsync(ImportSessionStatus.Draft);
        var first = await SeedItemAsync(session.Id, position: 0);
        var second = await SeedItemAsync(session.Id, position: 1);

        await _repository.SetItemPositionsAsync([(first.Id, 1), (second.Id, 0)]);

        var loaded = await _repository.GetByIdAsync(session.Id);
        Assert.That(loaded!.Items.OrderBy(i => i.Position).Select(i => i.Id),
            Is.EqualTo([second.Id, first.Id]));
    }

    [Test]
    public async Task SetItemSkipped_AndDeleteItem_AreScopedToOneRow()
    {
        var session = await SeedSessionAsync(ImportSessionStatus.InProgress);
        var first = await SeedItemAsync(session.Id, position: 0);
        var second = await SeedItemAsync(session.Id, position: 1);

        await _repository.SetItemSkippedAsync(first.Id, true);
        await _repository.DeleteItemAsync(second.Id);

        var loaded = await _repository.GetByIdAsync(session.Id);
        Assert.That(loaded!.Items, Has.Count.EqualTo(1));
        Assert.That(loaded.Items.Single().Skipped, Is.True);
    }

    [Test]
    public async Task DeletingTheWorld_TakesTheSessionAndItsItems()
    {
        var session = await SeedSessionAsync(ImportSessionStatus.Draft);
        await SeedItemAsync(session.Id, position: 0);

        Context.Worlds.Remove(Context.Worlds.Single(w => w.Id == _worldId));
        await Context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Context.ImportSessions.Any(), Is.False);
            Assert.That(Context.ImportSessionItems.Any(), Is.False);
        });
    }

    private async Task<ImportSession> SeedSessionAsync(ImportSessionStatus status)
    {
        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            CreatedByUserId = _gmId,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateAsync(session);
        return session;
    }

    private async Task<ImportSessionItem> SeedItemAsync(Guid sessionId, int position)
    {
        var item = new ImportSessionItem
        {
            Id = Guid.NewGuid(),
            ImportSessionId = sessionId,
            SourceId = Guid.NewGuid(),
            Position = position,
            Skipped = false
        };

        await _repository.AddItemAsync(item);
        return item;
    }
}
