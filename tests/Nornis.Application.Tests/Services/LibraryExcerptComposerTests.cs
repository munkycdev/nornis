using Nornis.Application.Services;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LibraryExcerptComposerTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();

    private static LibraryChunkHit Chunk(int ord, int page, string text) =>
        new(Guid.NewGuid(), DocumentId, "Player's Guide", ord, page, text, 0d);

    [Test]
    public void BuildTitle_WithArtifactAndRange_NamesEntryDocumentAndPages()
    {
        var title = LibraryExcerptComposer.BuildTitle("Thistlehold", "Player's Guide", 42, 45);

        Assert.That(title, Is.EqualTo("Thistlehold — Player's Guide, pp. 42–45"));
    }

    [Test]
    public void BuildTitle_NoArtifactSinglePage_NamesDocumentAndPage()
    {
        var title = LibraryExcerptComposer.BuildTitle(null, "Player's Guide", 42, 42);

        Assert.That(title, Is.EqualTo("Player's Guide, p. 42"));
    }

    [Test]
    public void BuildTitle_OverTheSourceTitleLimit_IsCutToFit()
    {
        var title = LibraryExcerptComposer.BuildTitle(new string('T', 150), new string('D', 150), 1, 2);

        Assert.That(title, Has.Length.EqualTo(LibraryExcerptComposer.MaxTitleLength));
        Assert.That(title, Does.EndWith("…"));
    }

    [Test]
    public void BuildBody_OpensWithTheFilingLine_NamingTheEntry()
    {
        var body = LibraryExcerptComposer.BuildBody("Thistlehold", "Player's Guide", 42, 45,
            [Chunk(0, 42, "The city of Thistlehold sits on the river.")], overlapChars: 4);

        var firstLine = body.Split('\n')[0];
        Assert.That(firstLine, Is.EqualTo("Excerpt from “Player's Guide”, pp. 42–45, filed for the codex entry “Thistlehold”."));
        Assert.That(body, Does.EndWith("The city of Thistlehold sits on the river."));
    }

    [Test]
    public void BuildBody_NoArtifact_FilingLineOmitsTheEntryClause()
    {
        var body = LibraryExcerptComposer.BuildBody(null, "Player's Guide", 42, 42,
            [Chunk(0, 42, "text")], overlapChars: 0);

        Assert.That(body.Split('\n')[0], Is.EqualTo("Excerpt from “Player's Guide”, p. 42."));
    }

    [Test]
    public void BuildBody_StripsAnOverlapTheNextChunkVerifiablyStartsWith()
    {
        var body = LibraryExcerptComposer.BuildBody(null, "Player's Guide", 1, 1,
            [Chunk(0, 1, "abcdefgh"), Chunk(1, 1, "efghijkl")], overlapChars: 4);

        Assert.That(body, Does.EndWith("abcdefgh\n\nijkl"));
        Assert.That(body, Does.Not.Contain("efghijkl"));
    }

    [Test]
    public void BuildBody_LeavesAChunkAloneWhenItDoesNotStartWithThePreviousTail()
    {
        var body = LibraryExcerptComposer.BuildBody(null, "Player's Guide", 1, 1,
            [Chunk(0, 1, "abcdefgh"), Chunk(1, 1, "xxxxijkl")], overlapChars: 4);

        Assert.That(body, Does.EndWith("abcdefgh\n\nxxxxijkl"));
    }

    [Test]
    public void BuildBody_ZeroOverlap_NeverStrips()
    {
        var body = LibraryExcerptComposer.BuildBody(null, "Player's Guide", 1, 1,
            [Chunk(0, 1, "abcdefgh"), Chunk(1, 1, "efghijkl")], overlapChars: 0);

        Assert.That(body, Does.EndWith("abcdefgh\n\nefghijkl"));
    }
}
