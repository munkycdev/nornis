using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// This helper decides whether apply-time dedup binds a new source's provenance to existing
/// canon, so its conservatism is the feature: case and whitespace are noise, everything else
/// is a different name until a human says otherwise.
/// </summary>
[TestFixture]
public class ArtifactNameKeyTests
{
    [TestCase("Black Harbor", "black harbor")]
    [TestCase("Black Harbor", "  Black Harbor  ")]
    [TestCase("Black  Harbor", "Black Harbor")]
    [TestCase("Black\tHarbor", "Black Harbor")]
    [TestCase("Black \n Harbor", "BLACK HARBOR")]
    public void EquivalentNames(string a, string b)
    {
        Assert.That(ArtifactNameKey.AreEquivalent(a, b), Is.True);
        Assert.That(ArtifactNameKey.Normalize(a), Is.EqualTo(ArtifactNameKey.Normalize(b)));
    }

    [TestCase("Salt Factor", "The Salt Factor")]
    [TestCase("Black Harbor", "Blackharbor")]
    [TestCase("Captain Voss", "Voss")]
    [TestCase("Black Harbor", "Black Harbour")]
    public void DistinctNames(string a, string b)
    {
        Assert.That(ArtifactNameKey.AreEquivalent(a, b), Is.False);
    }

    [Test]
    public void ArticlesAreNotStripped()
    {
        // Deliberate: "Salt Factor" vs "The Salt Factor" is a merge decision for the GM, not
        // something an automatic backstop gets to make on their behalf.
        Assert.That(ArtifactNameKey.AreEquivalent("The Salt Factor", "Salt Factor"), Is.False);
        Assert.That(ArtifactNameKey.AreEquivalent("A Crown of Ash", "Crown of Ash"), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BlankNamesMatchNothing(string? blank)
    {
        Assert.That(ArtifactNameKey.AreEquivalent(blank, "Black Harbor"), Is.False);
        Assert.That(ArtifactNameKey.AreEquivalent("Black Harbor", blank), Is.False);
        Assert.That(ArtifactNameKey.AreEquivalent(blank, blank), Is.False,
            "two unnamed artifacts are not duplicates of each other");
        Assert.That(ArtifactNameKey.Collapse(blank), Is.Empty);
    }

    [Test]
    public void CollapsePreservesCase()
    {
        Assert.That(ArtifactNameKey.Collapse("  the   Salt   FACTOR "), Is.EqualTo("the Salt FACTOR"));
    }

    [Test]
    public void ExactCaseEquivalence_IgnoresOnlyWhitespace()
    {
        Assert.That(ArtifactNameKey.AreExactCaseEquivalent("Black  Harbor", " Black Harbor "), Is.True);
        Assert.That(ArtifactNameKey.AreExactCaseEquivalent("Black Harbor", "black harbor"), Is.False);
        Assert.That(ArtifactNameKey.AreExactCaseEquivalent("", ""), Is.False);
    }
}
