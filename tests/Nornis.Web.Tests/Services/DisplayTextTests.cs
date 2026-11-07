using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class DisplayTextTests
{
    [TestCase("LocatedIn", "located in")]
    [TestCase("ConnectedTo", "connected to")]
    [TestCase("PartOf", "part of")]
    [TestCase("Rules", "rules")]
    [TestCase("allied with", "allied with")]
    [TestCase(" Advances ", "advances")]
    public void Predicate_ReadsAsPartOfASentence(string type, string expected)
    {
        Assert.That(DisplayText.Predicate(type), Is.EqualTo(expected));
    }

    [Test]
    public void Truncate_KeepsTheEllipsisInsideTheLimit()
    {
        Assert.That(DisplayText.Truncate("The Vesper Bell mystery", 10), Has.Length.EqualTo(10).And.EndsWith("…"));
    }
}
