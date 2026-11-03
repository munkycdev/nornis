using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// The Library's view (feature 36): the Shelf, one Adventure's documents, the chips' counts and
/// the Adventure menu. The category and the shelf rule come from the API; these pin how the page
/// combines them.
/// </summary>
[TestFixture]
public class LibraryFilterTests
{
    private static readonly Guid Spa = Guid.NewGuid();
    private static readonly Guid Empty = Guid.NewGuid();

    private static readonly LibraryDocumentDto[] Documents =
    [
        Doc("Harbor map", "Map", minutesAgo: 1),
        Doc("Spa booklet", "Print", minutesAgo: 2, print: Spa),
        Doc("Spa handout", "Handout", minutesAgo: 3, linked: Spa),
        Doc("Player's Guide", "Book", minutesAgo: 4),
    ];

    [Test]
    public void TheShelf_LeavesOutLinkedUploads_ButKeepsPrints()
    {
        var shown = LibraryFilter.Apply(Documents, new LibraryView());

        Assert.That(shown.Select(d => d.Title), Is.EqualTo(["Harbor map", "Spa booklet", "Player's Guide"]));
    }

    [Test]
    public void AnAdventure_ShowsItsUploadsAndItsPrints()
    {
        var shown = LibraryFilter.Apply(Documents, new LibraryView(Scope: LibraryScope.ForAdventure(Spa)));

        Assert.That(shown.Select(d => d.Title), Is.EqualTo(["Spa booklet", "Spa handout"]));
    }

    [Test]
    public void Everything_ShowsEveryDocument()
    {
        Assert.That(LibraryFilter.Apply(Documents, new LibraryView(Scope: LibraryScope.Everything)), Has.Count.EqualTo(4));
    }

    [Test]
    public void Counts_FollowTheScope_AndLeaveOutEmptyCategories()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryFilter.Counts(Documents, LibraryScope.Shelf),
                Is.EqualTo([("Book", 1), ("Map", 1), ("Print", 1)]));
            Assert.That(LibraryFilter.Counts(Documents, LibraryScope.ForAdventure(Spa)),
                Is.EqualTo([("Handout", 1), ("Print", 1)]));
            Assert.That(LibraryFilter.Total(Documents, LibraryScope.Shelf), Is.EqualTo(3));
        });
    }

    [Test]
    public void TheMenu_OffersOnlyAdventuresWithDocuments()
    {
        AdventureSummaryDto[] adventures =
        [
            new(Spa, "The Slurping Spa", null, null, 3, 3, null, DateTimeOffset.UtcNow, false),
            new(Empty, "Nothing filed", null, null, 1, 0, null, DateTimeOffset.UtcNow, false),
        ];

        Assert.That(LibraryFilter.AdventuresWithDocuments(Documents, adventures).Select(a => a.Id), Is.EqualTo([Spa]));
    }

    [Test]
    public void SearchAndCategory_Combine_AndTitleSortIsAlphabetical()
    {
        var view = new LibraryView(Scope: LibraryScope.Everything, Search: " spa ", Sort: LibrarySort.Title);

        Assert.Multiple(() =>
        {
            Assert.That(LibraryFilter.Apply(Documents, view).Select(d => d.Title), Is.EqualTo(["Spa booklet", "Spa handout"]));
            Assert.That(LibraryFilter.Apply(Documents, view with { Category = "Handout" }).Select(d => d.Title),
                Is.EqualTo(["Spa handout"]));
        });
    }

    [TestCase("Book", "Sourcebook")]
    [TestCase("Map", "Map")]
    [TestCase("Handout", "Handout")]
    [TestCase("Other", "Other")]
    public void AChosenCategory_IsStoredAsItsKind(string category, string kind)
    {
        Assert.That(LibraryDisplay.KindFor(category), Is.EqualTo(kind));
    }

    [Test]
    public void TheCategoriesAGmCanChoose_NeverIncludePrint()
    {
        Assert.That(LibraryDisplay.ChoosableCategories.Select(c => c.Category), Does.Not.Contain(LibraryDocumentDto.PrintCategory));
    }

    private static LibraryDocumentDto Doc(string title, string category, int minutesAgo, Guid? linked = null, Guid? print = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), title, "f.png", "image/png", 1, "Map", "GMOnly", "Stored", null, 0, null,
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), DateTimeOffset.UtcNow,
        AdventureId: print, Category: category, LinkedAdventureId: linked, OnShelf: linked is null);
}
