using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

/// <summary>Library rows from a world's Bibliotaph collection (feature 31, Requirement 3).</summary>
[TestFixture]
public class BibliotaphLibrarySyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    [Test]
    public void A_new_book_becomes_a_GMOnly_Bibliotaph_row()
    {
        var (added, changed) = Plan([], [Book("e1", "Player's Guide")]);

        var row = added.Single();
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.Empty);
            Assert.That(row.Origin, Is.EqualTo(LibraryDocumentOrigin.Bibliotaph));
            Assert.That(row.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That((row.Title, row.RemoteTitle, row.BibliotaphEntryId, row.BibliotaphDocumentId),
                Is.EqualTo(("Player's Guide", "Player's Guide", "e1", "d-e1")));
            Assert.That(row.Kind, Is.EqualTo(LibraryDocumentKind.Sourcebook));
            Assert.That(row.Status, Is.EqualTo(LibraryDocumentStatus.Stored));
            Assert.That(row.UploadedByUserId, Is.EqualTo(Creator));
        });
    }

    [Test]
    public void An_untouched_title_follows_Bibliotaph_and_a_GMs_rename_stays()
    {
        var untouched = Row("e1", title: "Old", remote: "Old");
        var renamed = Row("e2", title: "Our Guide", remote: "Old Two");

        var (_, changed) = Plan([untouched, renamed], [Book("e1", "New"), Book("e2", "New Two")]);

        Assert.That(changed, Has.Count.EqualTo(2));
        Assert.That((untouched.Title, untouched.RemoteTitle), Is.EqualTo(("New", "New")));
        Assert.That((renamed.Title, renamed.RemoteTitle), Is.EqualTo(("Our Guide", "New Two")));
    }

    [Test]
    public void Nothing_changed_writes_nothing()
    {
        var row = Row("e1", title: "Guide", remote: "Guide");

        var (added, changed) = Plan([row], [Book("e1", "Guide")]);

        Assert.That(added, Is.Empty);
        Assert.That(changed, Is.Empty);
    }

    [Test]
    public void A_book_that_left_is_removed_and_comes_back_GMOnly()
    {
        var shared = Row("e1", title: "Guide", remote: "Guide");
        shared.Visibility = VisibilityScope.PartyVisible;

        Plan([shared], []);
        Assert.That(shared.RemovedAt, Is.EqualTo(Now));

        Plan([shared], [Book("e1", "Guide")]);
        Assert.That(shared.RemovedAt, Is.Null);
        Assert.That(shared.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
    }

    [Test]
    public void A_catalogued_upload_is_matched_by_its_file_id_and_appears_once()
    {
        var upload = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Title = "My upload",
            Origin = LibraryDocumentOrigin.Upload,
            Kind = LibraryDocumentKind.Sourcebook,
            Status = LibraryDocumentStatus.Indexed,
        };

        var (added, changed) = Plan([upload], [Book("e9", "From Nornis", upload.Id)]);

        Assert.That(added, Is.Empty);
        Assert.That(changed.Single(), Is.SameAs(upload));
        Assert.That(upload.BibliotaphEntryId, Is.EqualTo("e9"));
        Assert.That(upload.Title, Is.EqualTo("My upload"), "an upload's title is the GM's");

        Plan([upload], []);
        Assert.That(upload.BibliotaphEntryId, Is.Null, "uncatalogued, it is just an upload again");
        Assert.That(upload.RemovedAt, Is.Null);
    }

    [Test]
    public async Task Reconcile_is_throttled_per_world_and_a_failure_keeps_the_rows()
    {
        var documents = new InMemoryLibraryDocumentRepository();
        var client = Substitute.For<IBibliotaphClient>();
        client.ListBooksAsync(Creator, WorldId, Arg.Any<CancellationToken>())
            .Returns(BibliotaphResult<BibliotaphBookList>.Fail(BibliotaphFailure.Opening));
        var owners = Substitute.For<IBibliotaphConnectionResolver>();
        owners.ForWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(new BibliotaphWorldOwner(
            WorldId, Creator, "Dave", new BibliotaphConnection { UserId = Creator }));
        var clock = new Clock();
        var sync = new BibliotaphLibrarySync(owners, client, documents, new BibliotaphReconcileThrottle(),
            Options.Create(new BibliotaphOptions()), clock, NullLogger<BibliotaphLibrarySync>.Instance);

        var first = await sync.ReconcileAsync(WorldId, CancellationToken.None);
        var second = await sync.ReconcileAsync(WorldId, CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(61);
        await sync.ReconcileAsync(WorldId, CancellationToken.None);

        Assert.That(first, Is.EqualTo(new BibliotaphShelf(BibliotaphShelfStates.Opening, "Dave")));
        Assert.That(second, Is.EqualTo(first), "within 60 s the last answer stands");
        await client.Received(2).ListBooksAsync(Creator, WorldId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task An_unconnected_creator_reads_nothing()
    {
        var client = Substitute.For<IBibliotaphClient>();
        var owners = Substitute.For<IBibliotaphConnectionResolver>();
        owners.ForWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(new BibliotaphWorldOwner(WorldId, Creator, "", null));
        var sync = new BibliotaphLibrarySync(owners, client, new InMemoryLibraryDocumentRepository(), new BibliotaphReconcileThrottle(),
            Options.Create(new BibliotaphOptions()), new Clock(), NullLogger<BibliotaphLibrarySync>.Instance);

        var shelf = await sync.ReconcileAsync(WorldId, CancellationToken.None);

        Assert.That(shelf, Is.EqualTo(BibliotaphShelf.None));
        await client.DidNotReceiveWithAnyArgs().ListBooksAsync(default, default, default);
    }

    private static (IReadOnlyList<LibraryDocument> Added, IReadOnlyList<LibraryDocument> Changed) Plan(
        IReadOnlyList<LibraryDocument> rows, IReadOnlyList<BibliotaphBook> books) =>
        BibliotaphLibrarySync.Plan(WorldId, Creator, rows, books, Now);

    private static BibliotaphBook Book(string entryId, string title, Guid? nornisFileId = null) =>
        new(entryId, $"d-{entryId}", title, null, null, null, 100, "pdf", null, nornisFileId);

    private static LibraryDocument Row(string entryId, string title, string remote) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Origin = LibraryDocumentOrigin.Bibliotaph,
        BibliotaphEntryId = entryId,
        BibliotaphDocumentId = $"d-{entryId}",
        Title = title,
        RemoteTitle = remote,
        PageCount = 100,
        Kind = LibraryDocumentKind.Sourcebook,
        Status = LibraryDocumentStatus.Stored,
        Visibility = VisibilityScope.GMOnly,
    };

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BibliotaphLibrarySyncTests.Now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
