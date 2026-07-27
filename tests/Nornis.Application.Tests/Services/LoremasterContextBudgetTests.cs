using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The Ask prompt had no upper bound on its size: <c>MaxContextTokens</c> existed in
/// configuration but was referenced nowhere, and session bodies (validated up to 100,000
/// characters, three of them per ask) rode along untruncated. That made the cost of a single
/// question unbounded — including for anonymous public asks, where a per-world monthly cap is
/// supposed to make spend predictable.
/// </summary>
[TestFixture]
public class LoremasterContextBudgetTests
{
    private static KnowledgeSession Session(string text) => new()
    {
        Id = Guid.NewGuid(),
        Title = "The Black Harbor Interrogation",
        Date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
        Text = text,
        ReferenceId = "session:1",
    };

    private static KnowledgePassage Passage(string text) => new()
    {
        ChunkId = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        DocumentTitle = "Player's Guide",
        Page = 42,
        Text = text,
        ReferenceId = "passage:1",
    };

    private static KnowledgeContext Context(
        IReadOnlyList<KnowledgeSession>? sessions = null,
        IReadOnlyList<KnowledgePassage>? passages = null) => new()
        {
            Artifacts = [],
            Facts = [],
            Relationships = [],
            SourceReferences = [],
            Sessions = sessions ?? [],
            Passages = passages ?? [],
        };

    private static KnowledgeContext ContextWithSession(string text) => Context(sessions: [Session(text)]);

    [Test]
    public void SessionText_UnderTheCap_IsUntouched()
    {
        var formatted = LoremasterService.FormatKnowledgeContext(
            ContextWithSession("We questioned Captain Voss."), maxSessionChars: 4_000);

        Assert.That(formatted, Does.Contain("We questioned Captain Voss."));
        Assert.That(formatted, Does.Not.Contain("[session record truncated]"));
    }

    [Test]
    public void SessionText_OverTheCap_IsCutAndMarked()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 5_000));

        var formatted = LoremasterService.FormatKnowledgeContext(
            ContextWithSession(longText), maxSessionChars: 500);

        Assert.That(formatted, Does.Contain("[session record truncated]"),
            "an unmarked cut reads to the model as a complete record, so it answers confidently "
            + "about a session it only half saw");
        Assert.That(formatted.Length, Is.LessThan(longText.Length));
    }

    [Test]
    public void SessionTruncation_CutsOnAWordBoundary()
    {
        var text = string.Join(" ", Enumerable.Repeat("alpha", 400));

        var truncated = LoremasterService.TruncateSessionText(text, 100);
        var body = truncated.Replace("\n[session record truncated]", string.Empty);

        Assert.That(body, Does.Not.EndWith("alph"), "the cut must not land mid-word");
        Assert.That(body.Split(' '), Is.All.EqualTo("alpha"));
    }

    [Test]
    public void SessionTruncation_PrefersAParagraphBreak()
    {
        var text = "First paragraph of the recap.\n" + new string('x', 200);

        var truncated = LoremasterService.TruncateSessionText(text, 120);

        Assert.That(truncated, Does.StartWith("First paragraph of the recap."));
        Assert.That(truncated, Does.Not.Contain("xx"));
    }

    [Test]
    public void UnlimitedByDefault_SoRawFormattingIsUnchanged()
    {
        // The parameters default to unlimited; only the service passes real budgets. This keeps
        // the many formatting tests that call the single-argument overload meaningful.
        var text = new string('y', 50_000);

        Assert.That(LoremasterService.TruncateSessionText(text, int.MaxValue), Is.EqualTo(text));
    }

    [Test]
    public void ContextBudget_StopsAppendingAndSaysSo()
    {
        var context = Context(
            sessions: [Session(new string('z', 8_000))],
            passages: [Passage("A rule that should never be reached.")]);

        // A budget small enough that the session alone exhausts it.
        var formatted = LoremasterService.FormatKnowledgeContext(
            context, maxSessionChars: int.MaxValue, maxContextTokens: 100);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("[context truncated"));
            Assert.That(formatted, Does.Not.Contain("A rule that should never be reached."),
                "library passages are the least valuable context and must be the first to go");
        });
    }

    [Test]
    public void ContextBudget_KeepsEverythingWhenItFits()
    {
        var context = Context(
            sessions: [Session("A brief recap.")],
            passages: [Passage("A rule that should survive.")]);

        var formatted = LoremasterService.FormatKnowledgeContext(
            context, maxSessionChars: 4_000, maxContextTokens: 8_000);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("A rule that should survive."));
            Assert.That(formatted, Does.Not.Contain("[context truncated"));
        });
    }
}
