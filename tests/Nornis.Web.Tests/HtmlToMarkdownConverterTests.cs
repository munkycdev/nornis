using NUnit.Framework;
using Nornis.Web.Services;

namespace Nornis.Web.Tests;

[TestFixture]
public class HtmlToMarkdownConverterTests
{
    [Test]
    public void Convert_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.That(HtmlToMarkdownConverter.Convert(null), Is.Empty);
        Assert.That(HtmlToMarkdownConverter.Convert(""), Is.Empty);
        Assert.That(HtmlToMarkdownConverter.Convert("   "), Is.Empty);
    }

    [Test]
    public void Convert_PlainParagraphs_SeparatedByBlankLine()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<p>First.</p><p>Second.</p>");

        Assert.That(markdown, Is.EqualTo("First.\n\nSecond."));
    }

    [TestCase("<h1>Title</h1>", "# Title")]
    [TestCase("<h2>Sub</h2>", "## Sub")]
    [TestCase("<h3>Deep</h3>", "### Deep")]
    [TestCase("<h6>Deepest</h6>", "###### Deepest")]
    public void Convert_Headings(string html, string expected)
    {
        Assert.That(HtmlToMarkdownConverter.Convert(html), Is.EqualTo(expected));
    }

    [Test]
    public void Convert_InlineFormatting_BoldItalicStrike()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<p><strong>bold</strong> and <em>italic</em> and <s>gone</s></p>");

        Assert.That(markdown, Is.EqualTo("**bold** and *italic* and ~~gone~~"));
    }

    [Test]
    public void Convert_Link_BecomesMarkdownLink()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<p>See <a href=\"https://example.com\">the docs</a>.</p>");

        Assert.That(markdown, Is.EqualTo("See [the docs](https://example.com)."));
    }

    [Test]
    public void Convert_BulletList()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<ul><li>One</li><li>Two</li></ul>");

        Assert.That(markdown, Is.EqualTo("- One\n- Two"));
    }

    [Test]
    public void Convert_OrderedList()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<ol><li>First</li><li>Second</li></ol>");

        Assert.That(markdown, Is.EqualTo("1. First\n2. Second"));
    }

    [Test]
    public void Convert_NestedList_IndentsChildren()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<ul><li>Parent<ul><li>Child</li></ul></li><li>Sibling</li></ul>");

        Assert.That(markdown, Is.EqualTo("- Parent\n  - Child\n- Sibling"));
    }

    [Test]
    public void Convert_ListItemWithBold_KeepsFormatting()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<ul><li><strong>Koves</strong> escapes</li></ul>");

        Assert.That(markdown, Is.EqualTo("- **Koves** escapes"));
    }

    [Test]
    public void Convert_Blockquote()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<blockquote><p>Wise words</p></blockquote>");

        Assert.That(markdown, Is.EqualTo("> Wise words"));
    }

    [Test]
    public void Convert_CodeBlock_And_InlineCode()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<pre><code>roll 2d6</code></pre><p>use <code>advantage</code></p>");

        Assert.That(markdown, Does.Contain("```\nroll 2d6\n```"));
        Assert.That(markdown, Does.Contain("use `advantage`"));
    }

    [Test]
    public void Convert_HorizontalRule()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<p>Above</p><hr><p>Below</p>");

        Assert.That(markdown, Is.EqualTo("Above\n\n---\n\nBelow"));
    }

    [Test]
    public void Convert_Table_BecomesPipeTable_WithHeaderSeparator()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<table><tbody>" +
            "<tr><th><p>Name</p></th><th><p>Role</p></th></tr>" +
            "<tr><td><p>Koves</p></td><td><p>Captive</p></td></tr>" +
            "</tbody></table>");

        Assert.That(markdown, Is.EqualTo("| Name | Role |\n| --- | --- |\n| Koves | Captive |"));
    }

    [Test]
    public void Convert_Table_WithColgroup_AndInlineMarks()
    {
        // Resizable TipTap tables emit a <colgroup>; cell marks must survive as markdown.
        var markdown = HtmlToMarkdownConverter.Convert(
            "<table><colgroup><col><col></colgroup><tbody>" +
            "<tr><th><p>Item</p></th><th><p>Note</p></th></tr>" +
            "<tr><td><p><strong>Whip</strong></p></td><td><p>a pipe \\| here</p></td></tr>" +
            "</tbody></table>");

        Assert.That(markdown, Does.Contain("| Item | Note |"));
        Assert.That(markdown, Does.Contain("| --- | --- |"));
        Assert.That(markdown, Does.Contain("| **Whip** |"));
    }

    [Test]
    public void Convert_TableCellWithPipe_EscapesPipe()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<table><tbody><tr><th><p>a|b</p></th></tr><tr><td><p>c</p></td></tr></tbody></table>");

        Assert.That(markdown, Does.Contain("| a\\|b |"));
    }

    [Test]
    public void Convert_HtmlEntities_Decoded()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<p>Fish &amp; chips &gt; stew</p>");

        Assert.That(markdown, Is.EqualTo("Fish & chips > stew"));
    }

    [Test]
    public void Convert_LineBreaks_BecomeNewlines()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<p>line one<br>line two</p>");

        Assert.That(markdown, Is.EqualTo("line one\nline two"));
    }

    [Test]
    public void Convert_FullDocument_RoundsTrip()
    {
        var markdown = HtmlToMarkdownConverter.Convert(
            "<h2>Session 12</h2>" +
            "<p>The party met <strong>Malliano</strong> at the docks.</p>" +
            "<ul><li>Icara scouted ahead</li><li>Ugma guarded the rear</li></ul>");

        Assert.That(markdown, Is.EqualTo(
            "## Session 12\n\nThe party met **Malliano** at the docks.\n\n- Icara scouted ahead\n- Ugma guarded the rear"));
    }
}
