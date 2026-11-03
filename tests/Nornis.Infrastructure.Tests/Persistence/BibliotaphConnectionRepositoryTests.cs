using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

[TestFixture]
public class BibliotaphConnectionRepositoryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private BibliotaphConnectionRepository _sut = null!;
    private User _user = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new BibliotaphConnectionRepository(Context);
        _user = new User
        {
            Id = Guid.NewGuid(),
            SignIns = { new UserSignIn { Subject = $"auth0|{Guid.NewGuid():N}" } },
            Username = "dave",
            Email = "dave@example.com",
            CreatedAt = Now,
            UpdatedAt = Now,
            RowVersion = [],
        };
        Context.Users.Add(_user);
        await Context.SaveChangesAsync();
    }

    [Test]
    public async Task Connecting_again_replaces_the_grant_and_starts_fresh()
    {
        var first = await _sut.UpsertAsync(_user.Id, "grant-1", Now);
        await _sut.MarkNeedsReconnectAsync(first.Id, Now.AddHours(1));
        await _sut.TouchLastUsedAsync(first.Id, Now.AddHours(1), Now.AddHours(1));

        var second = await _sut.UpsertAsync(_user.Id, "grant-2", Now.AddHours(2));

        var stored = await Context.BibliotaphConnections.AsNoTracking().SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(second.Id, Is.EqualTo(first.Id));
            Assert.That(stored.ProtectedRefreshToken, Is.EqualTo("grant-2"));
            Assert.That(stored.Status, Is.EqualTo(BibliotaphConnectionStatus.Connected));
            Assert.That(stored.ConnectedAt, Is.EqualTo(Now.AddHours(2)));
            Assert.That(stored.LastUsedAt, Is.Null);
        });
    }

    [Test]
    public async Task Needs_reconnect_is_marked_once()
    {
        var connection = await _sut.UpsertAsync(_user.Id, "grant", Now);

        await _sut.MarkNeedsReconnectAsync(connection.Id, Now.AddMinutes(1));
        await _sut.MarkNeedsReconnectAsync(connection.Id, Now.AddMinutes(2));

        var stored = await _sut.GetByUserIdAsync(_user.Id);
        Assert.That(stored!.Status, Is.EqualTo(BibliotaphConnectionStatus.NeedsReconnect));
        Assert.That(stored.StatusChangedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public async Task Last_used_is_written_at_most_every_few_minutes()
    {
        var connection = await _sut.UpsertAsync(_user.Id, "grant", Now);

        await _sut.TouchLastUsedAsync(connection.Id, Now, Now.AddMinutes(-5));
        await _sut.TouchLastUsedAsync(connection.Id, Now.AddMinutes(1), Now.AddMinutes(-4));
        var afterOneMinute = (await _sut.GetByUserIdAsync(_user.Id))!.LastUsedAt;
        await _sut.TouchLastUsedAsync(connection.Id, Now.AddMinutes(6), Now.AddMinutes(1));
        var afterSix = (await _sut.GetByUserIdAsync(_user.Id))!.LastUsedAt;

        Assert.That(afterOneMinute, Is.EqualTo(Now));
        Assert.That(afterSix, Is.EqualTo(Now.AddMinutes(6)));
    }

    [Test]
    public async Task Disconnect_deletes_and_a_second_delete_does_nothing()
    {
        var connection = await _sut.UpsertAsync(_user.Id, "grant", Now);

        await _sut.DeleteAsync(connection.Id);
        await _sut.DeleteAsync(connection.Id);

        Assert.That(await _sut.GetByUserIdAsync(_user.Id), Is.Null);
    }

    [Test]
    public async Task Deleting_a_document_deletes_who_opened_it()
    {
        var world = new World { Id = Guid.NewGuid(), Name = "Gribbits", CreatedAt = Now, UpdatedAt = Now, CreatedByUserId = _user.Id, RowVersion = [] };
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Title = "Player's Guide",
            Origin = LibraryDocumentOrigin.Bibliotaph,
            BibliotaphEntryId = "e1",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = VisibilityScope.PartyVisible,
            Status = LibraryDocumentStatus.Stored,
            UploadedByUserId = _user.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Context.Worlds.Add(world);
        Context.LibraryDocuments.Add(document);
        Context.LibraryReadingRecords.Add(new LibraryReadingRecord
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            LibraryDocumentId = document.Id,
            UserId = _user.Id,
            ViewerName = "dave",
            OpenedAt = Now,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        await new LibraryDocumentRepository(Context).DeleteAsync(document.Id);

        Assert.That(await Context.LibraryReadingRecords.AnyAsync(), Is.False);
    }
}
