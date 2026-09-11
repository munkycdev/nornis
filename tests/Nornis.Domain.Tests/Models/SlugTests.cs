using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// The slug rule names detail pages, so what it does to a name is what a person sees in the
/// address bar and pastes into Discord. These pin the shape: lossy, lowercase, hyphenated,
/// bounded, never empty.
/// </summary>
[TestFixture]
public class SlugTests
{
    [TestCase("The Ashen King", "the-ashen-king")]
    [TestCase("  Black   Harbor  ", "black-harbor")]
    [TestCase("Rook & Thorn", "rook-thorn")]
    [TestCase("Session 12: The Long Night", "session-12-the-long-night")]
    [TestCase("Café Émile — Août", "cafe-emile-aout")]
    [TestCase("Al", "al")]
    [TestCase("--already--slugged--", "already-slugged")]
    [TestCase("Straße", "stra-e")]
    [TestCase("Player's Handbook", "players-handbook")]
    [TestCase("Mira’s Reach", "miras-reach")]
    public void DerivesFromTheName(string name, string expected)
    {
        Assert.That(Slug.From(name, "artifact"), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("!!!")]
    [TestCase("龍の巣")]
    [TestCase(null)]
    public void NothingUsable_FallsBackToTheKind(string? name)
    {
        Assert.That(Slug.From(name, "Artifact"), Is.EqualTo("artifact"));
    }

    [Test]
    public void FallbackMustItselfBeUsable()
    {
        Assert.That(() => Slug.From("!!!", "???"), Throws.ArgumentException);
    }

    [Test]
    public void LongNames_AreCutAtTheLimit_WithoutATrailingHyphen()
    {
        var name = string.Join(' ', Enumerable.Repeat("word", 40));
        var slug = Slug.From(name, "artifact");

        Assert.That(slug.Length, Is.LessThanOrEqualTo(Slug.MaxLength));
        Assert.That(slug, Does.Not.EndWith("-"));
        Assert.That(Slug.IsValid(slug), Is.True);
    }

    [Test]
    public void DerivedSlugs_AreIdempotent()
    {
        var once = Slug.From("Lord Varen of Thale", "artifact");
        Assert.That(Slug.From(once, "artifact"), Is.EqualTo(once));
    }

    [TestCase("vespergale", true)]
    [TestCase("vespergale-reach", true)]
    [TestCase("ab", false)]
    [TestCase("-leading", false)]
    [TestCase("trailing-", false)]
    [TestCase("Upper", false)]
    [TestCase("under_score", false)]
    [TestCase(null, false)]
    public void IsValid_IsTheGmTypedRule(string? slug, bool expected)
    {
        Assert.That(Slug.IsValid(slug), Is.EqualTo(expected));
    }

    [Test]
    public void StoredWidth_LeavesRoomForACollisionSuffix()
    {
        // "-9999" is the widest suffix the assigner can add to a slug already at MaxLength.
        Assert.That(Slug.MaxStoredLength, Is.EqualTo(Slug.MaxLength + "-9999".Length));
    }
}
