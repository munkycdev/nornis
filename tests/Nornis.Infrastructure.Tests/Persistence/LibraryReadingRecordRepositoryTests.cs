using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>Who opened which Bibliotaph book (feature 31): newest first, aged out in bounded batches.</summary>
[TestFixture]
public class LibraryReadingRecordRepositoryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Opens_list_newest_first_and_the_purge_takes_the_oldest_in_batches()
    {
        var user = new User { Id = Guid.NewGuid(), Auth0SubjectId = $"auth0|{Guid.NewGuid():N}", Username = "dave", Email = "d@example.com", CreatedAt = Now, UpdatedAt = Now, RowVersion = [] };
        var world = new World { Id = Guid.NewGuid(), Name = "Gribbits", CreatedAt = Now, UpdatedAt = Now, CreatedByUserId = user.Id, RowVersion = [] };
        var book = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Title = "Guide",
            Origin = LibraryDocumentOrigin.Bibliotaph,
            BibliotaphEntryId = "e1",
            Kind = LibraryDocumentKind.Sourcebook,
            Status = LibraryDocumentStatus.Stored,
            Visibility = VisibilityScope.PartyVisible,
            UploadedByUserId = user.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Context.Users.Add(user);
        Context.Worlds.Add(world);
        Context.LibraryDocuments.Add(book);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var sut = new LibraryReadingRecordRepository(Context);
        foreach (var daysAgo in new[] { 100, 95, 91, 10, 1 })
        {
            await sut.AddAsync(new LibraryReadingRecord
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                LibraryDocumentId = book.Id,
                UserId = user.Id,
                ViewerName = $"{daysAgo} days",
                OpenedAt = Now.AddDays(-daysAgo),
            });
        }

        var recent = await sut.ListRecentAsync(book.Id, 2);
        var firstPurge = await sut.PurgeOlderThanAsync(Now.AddDays(-90), 2);
        var secondPurge = await sut.PurgeOlderThanAsync(Now.AddDays(-90), 2);

        Assert.That(recent.Select(r => r.ViewerName), Is.EqualTo(["1 days", "10 days"]));
        Assert.That((firstPurge, secondPurge), Is.EqualTo((2, 1)));
        Assert.That(await Context.LibraryReadingRecords.CountAsync(), Is.EqualTo(2));
    }
}
