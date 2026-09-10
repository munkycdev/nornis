using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Deleting a Library document leaves the excerpts filed from it in the record (feature 26,
/// Requirement 5). The text was copied at filing, so the source stands on its own; only the
/// link back to the document is cleared — by <c>LibraryDocumentRepository.DeleteAsync</c>,
/// because the FK is Restrict (Worlds already cascades to both tables).
///
/// A database test because the constraint is a database fact: if the detach ever stops, the
/// symptom is a foreign key violation on an ordinary document delete.
/// </summary>
[TestFixture]
public class LibraryDocumentDetachesExcerptsTests : IntegrationTestBase
{
    private LibraryDocumentRepository _sut = null!;
    private LibraryDocument _document = null!;
    private Source _excerpt = null!;
    private Source _sessionNote = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new LibraryDocumentRepository(Context);

        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|k-{tag}",
            Username = $"kelda-{tag}",
            Email = $"k-{tag}@example.com",
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

        _document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Title = "Player's Guide",
            FileName = "guide.pdf",
            ContentType = "application/pdf",
            BlobPath = $"worlds/{world.Id}/library/guide.pdf",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = VisibilityScope.PartyVisible,
            Status = LibraryDocumentStatus.Indexed,
            UploadedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        Context.LibraryDocuments.Add(_document);

        _excerpt = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = SourceType.LibraryExcerpt,
            Title = "Thistlehold — Player's Guide, pp. 42–45",
            Body = "Excerpt from “Player's Guide”, pp. 42–45.\n\nThistlehold sits on the river.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = user.Id,
            CreatedAt = now,
            LibraryDocumentId = _document.Id,
            LibraryPageFrom = 42,
            LibraryPageTo = 45
        };
        _sessionNote = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = SourceType.SessionNote,
            Title = "Session 4",
            Body = "We reached Thistlehold at dusk.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = user.Id,
            CreatedAt = now
        };
        Context.Sources.AddRange(_excerpt, _sessionNote);

        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Test]
    public async Task DeleteAsync_KeepsTheExcerpt_WithItsTextAndPages_AndClearsTheLink()
    {
        await _sut.DeleteAsync(_document.Id);
        Context.ChangeTracker.Clear();

        Assert.That(await Context.LibraryDocuments.AnyAsync(d => d.Id == _document.Id), Is.False);

        var excerpt = await Context.Sources.SingleAsync(s => s.Id == _excerpt.Id);
        Assert.That(excerpt.LibraryDocumentId, Is.Null);
        Assert.That(excerpt.Type, Is.EqualTo(SourceType.LibraryExcerpt));
        Assert.That(excerpt.Title, Is.EqualTo("Thistlehold — Player's Guide, pp. 42–45"));
        Assert.That(excerpt.Body, Does.Contain("Thistlehold sits on the river."));
        Assert.That((excerpt.LibraryPageFrom, excerpt.LibraryPageTo), Is.EqualTo((42, 45)));

        var note = await Context.Sources.SingleAsync(s => s.Id == _sessionNote.Id);
        Assert.That(note.LibraryDocumentId, Is.Null, "untouched sources stay untouched");
    }
}
