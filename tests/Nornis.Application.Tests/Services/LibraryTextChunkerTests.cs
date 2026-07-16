using NUnit.Framework;
using Nornis.Application.Services;
using Nornis.Application.Storage;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LibraryTextChunkerTests
{
    [Test]
    public void Chunk_EmptyPages_ReturnsNothing()
    {
        var chunks = LibraryTextChunker.Chunk([new PdfPageText(1, ""), new PdfPageText(2, "   ")], 100, 20);

        Assert.That(chunks, Is.Empty);
    }

    [Test]
    public void Chunk_SmallText_SingleChunkWithFirstPage()
    {
        var chunks = LibraryTextChunker.Chunk([new PdfPageText(3, "Hello world.")], 100, 20);

        Assert.That(chunks, Has.Count.EqualTo(1));
        Assert.That(chunks[0].Page, Is.EqualTo(3));
        Assert.That(chunks[0].Ord, Is.EqualTo(0));
        Assert.That(chunks[0].Text, Is.EqualTo("Hello world."));
    }

    [Test]
    public void Chunk_TextBeyondMaxChars_SplitsIntoOrderedChunks()
    {
        var paragraphs = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Paragraph {i} " + new string('x', 40)));
        var chunks = LibraryTextChunker.Chunk([new PdfPageText(1, paragraphs)], 120, 20);

        Assert.That(chunks.Count, Is.GreaterThan(1));
        Assert.That(chunks.Select(c => c.Ord), Is.EqualTo(Enumerable.Range(0, chunks.Count)));
        Assert.That(chunks.All(c => c.Text.Length <= 120 + 20 + 1), Is.True,
            "chunks stay near the max (content + carried overlap)");
    }

    [Test]
    public void Chunk_Overlap_CarriesTailIntoNextChunk()
    {
        var pageText = new string('a', 100) + "\n\n" + new string('b', 100);
        var chunks = LibraryTextChunker.Chunk([new PdfPageText(1, pageText)], 100, 30);

        Assert.That(chunks, Has.Count.EqualTo(2));
        Assert.That(chunks[1].Text, Does.StartWith(new string('a', 30)),
            "second chunk starts with the tail of the first");
    }

    [Test]
    public void Chunk_PageAttribution_ChunkStartsOnItsPage()
    {
        var pages = new[]
        {
            new PdfPageText(1, new string('a', 90)),
            new PdfPageText(2, new string('b', 90)),
        };
        var chunks = LibraryTextChunker.Chunk(pages, 100, 0);

        Assert.That(chunks, Has.Count.EqualTo(2));
        Assert.That(chunks[0].Page, Is.EqualTo(1));
        Assert.That(chunks[1].Page, Is.EqualTo(2));
    }

    [Test]
    public void Chunk_GiantParagraph_IsHardSplit()
    {
        var chunks = LibraryTextChunker.Chunk([new PdfPageText(1, new string('z', 350))], 100, 0);

        Assert.That(chunks.Count, Is.GreaterThanOrEqualTo(4));
        Assert.That(chunks.Sum(c => c.Text.Length), Is.EqualTo(350));
    }

    [Test]
    public void Chunk_BlankPagesBetweenContent_AreSkipped()
    {
        var pages = new[]
        {
            new PdfPageText(1, "First page."),
            new PdfPageText(2, "   "),
            new PdfPageText(3, "Third page."),
        };
        var chunks = LibraryTextChunker.Chunk(pages, 1000, 0);

        Assert.That(chunks, Has.Count.EqualTo(1));
        Assert.That(chunks[0].Text, Does.Contain("First page."));
        Assert.That(chunks[0].Text, Does.Contain("Third page."));
    }
}
