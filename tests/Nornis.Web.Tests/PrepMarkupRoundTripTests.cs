using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Prep's markup has to survive the editor: stored Markdown is rendered by Markdig, edited
/// in TipTap, and converted back by <see cref="HtmlToMarkdownConverter"/> on save. Each case
/// is checked from both HTML shapes the converter receives: Markdig's (the first load) and
/// TipTap's own <c>getHTML()</c>.
///
/// The TipTap HTML below is not hand-written. It was captured on 2026-11-02 by loading
/// <c>wwwroot/lib/tiptap.bundle.min.js</c> (TipTap 3.11.0) in headless Chromium with the
/// extensions <c>nornis-editor.js</c> configures, passing Markdig's HTML for the case to
/// <c>setContent</c> and reading <c>getHTML()</c> back. If the bundle is rebuilt, capture again.
/// </summary>
[TestFixture]
public class PrepMarkupRoundTripTests
{
    public static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(
            "> The steam hangs low over the water. A slime the size of a pony regards you.\n>\n> It burbles politely.",
            "<blockquote><p>The steam hangs low over the water. A slime the size of a pony regards you.</p><p>It burbles politely.</p></blockquote>")
            .SetName("ReadAloud_TwoParagraphs");

        yield return new TestCaseData(
            $"> {PrepMarkup.PropMarker}\n> A sealed letter, wax stamped with a grinning frog.\n> It reads: *Bring towels.*",
            "<blockquote><p>[!PROP]<br>A sealed letter, wax stamped with a grinning frog.<br>It reads: <em>Bring towels.</em></p></blockquote>")
            .SetName("Prop");

        yield return new TestCaseData(
            $"> {PrepMarkup.AskMarker}\n> What does your character smell first?",
            "<blockquote><p>[!ASK]<br>What does your character smell first?</p></blockquote>")
            .SetName("Ask");

        yield return new TestCaseData(
            $"```{PrepMarkup.StatBlockInfo}\nName: Bickle\nSize: Small\nHP: 14\nSpeed: 20 ft., swim 30 ft.\n```",
            "<pre><code class=\"language-statblock\">Name: Bickle\nSize: Small\nHP: 14\nSpeed: 20 ft., swim 30 ft.\n</code></pre>")
            .SetName("StatBlock");

        yield return new TestCaseData(
            "| Clue | Where |\n| --- | --- |\n| Towel | Lobby |\n| Key | Grotto |",
            "<table style=\"min-width: 50px;\"><colgroup><col style=\"min-width: 25px;\"><col style=\"min-width: 25px;\"></colgroup><tbody><tr><th colspan=\"1\" rowspan=\"1\"><p>Clue</p></th><th colspan=\"1\" rowspan=\"1\"><p>Where</p></th></tr><tr><td colspan=\"1\" rowspan=\"1\"><p>Towel</p></td><td colspan=\"1\" rowspan=\"1\"><p>Lobby</p></td></tr><tr><td colspan=\"1\" rowspan=\"1\"><p>Key</p></td><td colspan=\"1\" rowspan=\"1\"><p>Grotto</p></td></tr></tbody></table>")
            .SetName("PipeTable");
    }

    [TestCaseSource(nameof(Cases))]
    public void TipTapHtml_ConvertsBackToTheStoredMarkdown(string markdown, string tipTapHtml)
    {
        Assert.That(HtmlToMarkdownConverter.Convert(tipTapHtml), Is.EqualTo(markdown));
    }

    [TestCaseSource(nameof(Cases))]
    public void MarkdigHtml_ConvertsBackToTheStoredMarkdown(string markdown, string tipTapHtml)
    {
        _ = tipTapHtml;
        var html = MarkdownRenderer.ToHtml(markdown).Value;

        Assert.That(HtmlToMarkdownConverter.Convert(html), Is.EqualTo(markdown));
    }

    [TestCase("prop", "[!PROP]")]
    [TestCase("ask", "[!ASK]")]
    public void TheToolbarsMarkedBlocks_SaveWithTheMarkerAsTheirFirstLine(string button, string marker)
    {
        var html = button == "prop" ? PrepMarkup.PropBlockHtml() : PrepMarkup.AskBlockHtml();

        var lines = HtmlToMarkdownConverter.Convert(html).Split('\n');

        Assert.That(lines[0], Is.EqualTo($"> {marker}"));
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[1], Does.StartWith("> ").And.Length.GreaterThan(2));
    }

    [Test]
    public void TheToolbarsStatBlock_SavesAsAStatblockFence_InTheGrammar()
    {
        // The shape TipTap gives a code block with a language (see the StatBlock case above).
        var html = $"<pre><code class=\"language-{PrepMarkup.StatBlockInfo}\">{PrepMarkup.StatBlockTemplate}</code></pre>";

        var markdown = HtmlToMarkdownConverter.Convert(html);

        Assert.That(markdown, Is.EqualTo($"```statblock\n{PrepMarkup.StatBlockTemplate}\n```"));
        Assert.That(markdown, Does.StartWith("```statblock\n# Name\n*"), "a name line, then a subtitle (feature 35)");
    }

    [Test]
    public void TheToolbarsItem_SavesAsAnItemFence_InTheGrammar()
    {
        var html = $"<pre><code class=\"language-{PrepMarkup.ItemBlockInfo}\">{PrepMarkup.ItemBlockTemplate}</code></pre>";

        var markdown = HtmlToMarkdownConverter.Convert(html);

        Assert.That(markdown, Is.EqualTo($"```item\n{PrepMarkup.ItemBlockTemplate}\n```"));
        Assert.That(PrepMarkup.ItemBlockTemplate, Does.StartWith("# Name\n*").And.Contain("\n---\n"));
    }

    [Test]
    public void TheToolbarsGmNote_SavesAsAMarkedQuote()
    {
        var markdown = HtmlToMarkdownConverter.Convert(PrepMarkup.GmBlockHtml());
        var lines = markdown.Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo($"> {PrepMarkup.GmMarker}"));
            Assert.That(lines, Has.Length.EqualTo(2));
        });
    }

    /// <summary>
    /// The toolbar buttons' output as TipTap holds it after the insert, captured on 2026-11-02
    /// the same way as the cases above, by calling <c>nornisEditor.insertHtml</c> and
    /// <c>insertCodeBlock</c> with what <see cref="PrepMarkup"/> hands them, after an
    /// "Intro." paragraph. TipTap adds the empty trailing paragraph itself.
    /// </summary>
    [TestCase(
        "<p>Intro.</p><blockquote><p>[!ASK]<br>What do you ask the players?</p></blockquote><p></p>",
        "Intro.\n\n> [!ASK]\n> What do you ask the players?")]
    [TestCase(
        "<p>Intro.</p><pre><code class=\"language-statblock\">Name: \nDefense: \nHealth: \nSpeed: \nAttacks: </code></pre><p></p>",
        "Intro.\n\n```statblock\nName: \nDefense: \nHealth: \nSpeed: \nAttacks: \n```")]
    public void TheToolbarsInsertsInTipTap_SaveAsPrepMarkup(string tipTapHtml, string markdown) =>
        Assert.That(HtmlToMarkdownConverter.Convert(tipTapHtml), Is.EqualTo(markdown));

    [Test]
    public void ACodeBlockWithoutAnInfoString_StaysBare()
    {
        var markdown = HtmlToMarkdownConverter.Convert("<pre><code>roll 2d6\n</code></pre>");

        Assert.That(markdown, Is.EqualTo("```\nroll 2d6\n```"));
    }
}
