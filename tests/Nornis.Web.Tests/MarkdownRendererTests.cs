using NUnit.Framework;
using Nornis.Web.Services;

namespace Nornis.Web.Tests;

[TestFixture]
public class MarkdownRendererTests
{
    [Test]
    public void ToHtml_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.That(MarkdownRenderer.ToHtml(null).Value, Is.Empty);
        Assert.That(MarkdownRenderer.ToHtml("   ").Value, Is.Empty);
    }

    [Test]
    public void ToHtml_RendersHeadingsAndBold()
    {
        var html = MarkdownRenderer.ToHtml("## Session 12\n\nThe party met **Malliano**.").Value;

        Assert.That(html, Does.Contain("<h2>Session 12</h2>"));
        Assert.That(html, Does.Contain("<strong>Malliano</strong>"));
    }

    [Test]
    public void ToHtml_RawHtml_IsNeutralized()
    {
        // DisableHtml: user-typed markup must never reach the page as live HTML.
        var html = MarkdownRenderer.ToHtml("hello <script>alert(1)</script> world").Value;

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
    }

    [Test]
    public void ToHtml_SingleNewline_BecomesLineBreak()
    {
        // Legacy plain-text bodies rely on single newlines rendering as breaks.
        var html = MarkdownRenderer.ToHtml("line one\nline two").Value;

        Assert.That(html, Does.Contain("<br"));
    }

    [Test]
    public void ToHtml_PipeTable_RendersTable()
    {
        var html = MarkdownRenderer.ToHtml("| Name | Role |\n| --- | --- |\n| Koves | Captive |").Value;

        Assert.That(html, Does.Contain("<table>"));
        Assert.That(html, Does.Contain("<th>Name</th>"));
        Assert.That(html, Does.Contain("<td>Koves</td>"));
    }

    [Test]
    public void ToHtml_Strikethrough_Renders()
    {
        var html = MarkdownRenderer.ToHtml("this is ~~gone~~ now").Value;

        Assert.That(html, Does.Contain("<del>gone</del>"));
    }
}
