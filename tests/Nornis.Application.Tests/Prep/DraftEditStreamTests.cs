using Nornis.Application.Prep;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// What a Design turn streams to the chat (feature 41): its prose as it comes, and never the text
/// of an edit, however the pieces happen to split a line.
/// </summary>
[TestFixture]
public class DraftEditStreamTests
{
    private const string Answer =
        "I wrote the heist.\n\n````edit\n<<<\n===\n# The Heist\n\n```statblock\n# Muck Scavenger\n```\n\nThe end.\n>>>\n````\n\nWant footpads?";

    private static (string Prose, int Opened) Stream(IEnumerable<string> pieces)
    {
        var stream = new DraftEditStream();
        var prose = "";
        var opened = 0;
        foreach (var piece in pieces)
        {
            var (text, count) = stream.Push(piece);
            prose += text;
            opened += count;
        }

        var (rest, last) = stream.Flush();
        return (prose + rest, opened + last);
    }

    [Test]
    public void OnePiece_ShowsTheProse_AndHidesTheEdit()
    {
        var (prose, opened) = Stream([Answer]);

        Assert.Multiple(() =>
        {
            Assert.That(prose, Is.EqualTo("I wrote the heist.\n\n\nWant footpads?"));
            Assert.That(opened, Is.EqualTo(1));
        });
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(7)]
    public void AnySplit_ShowsTheSameProse(int size)
    {
        var pieces = Enumerable.Range(0, (Answer.Length + size - 1) / size)
            .Select(i => Answer.Substring(i * size, Math.Min(size, Answer.Length - (i * size))));

        Assert.That(Stream(pieces), Is.EqualTo(Stream([Answer])));
    }

    [Test]
    public void ProseStreams_BeforeItsLineEnds()
    {
        var stream = new DraftEditStream();

        Assert.That(stream.Push("I wrote").Prose, Is.EqualTo("I wrote"));
        Assert.That(stream.Push(" the heist.").Prose, Is.EqualTo(" the heist."));
    }

    [Test]
    public void ALineThatMayOpenAFence_WaitsForItsEnd()
    {
        var stream = new DraftEditStream();

        Assert.That(stream.Push("````ed").Prose, Is.Empty);
        Assert.That(stream.Push("it\n<<<\n").EditsOpened, Is.EqualTo(1));
    }

    [Test]
    public void AThreeBacktickEdit_IsNotClosedByTheStatBlockInsideIt()
    {
        var (prose, _) = Stream(["```edit\n<<<\n===\n```statblock\n# Slime\n```\nSECRET\n>>>\n```\nDone."]);

        Assert.That(prose, Is.EqualTo("Done."));
    }
}
