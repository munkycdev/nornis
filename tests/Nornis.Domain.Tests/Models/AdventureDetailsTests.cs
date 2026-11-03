using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// The four details a GM edits (feature 32): each bound holds at its limit and refuses one past
/// it, and a blank value is the same as no value.
/// </summary>
[TestFixture]
public class AdventureDetailsTests
{
    private const int ThisYear = 2026;

    [Test]
    public void NothingSet_IsValid() =>
        Assert.That(new AdventureDetails().Validate(ThisYear), Is.Null);

    [Test]
    public void Create_TrimsEachString_AndTurnsBlankIntoNull()
    {
        var details = AdventureDetails.Create("  Shadowdark  ", "   ", "\t1–3 ", 2023);

        Assert.That(details, Is.EqualTo(new AdventureDetails { GameSystem = "Shadowdark", Levels = "1–3", Year = 2023 }));
    }

    [Test]
    public void Create_OfNothing_IsTheEmptyDetails() =>
        Assert.That(AdventureDetails.Create(null, "", " ", null), Is.EqualTo(new AdventureDetails()));

    [TestCase(nameof(AdventureDetails.GameSystem), AdventureDetails.MaxGameSystemLength)]
    [TestCase(nameof(AdventureDetails.Edition), AdventureDetails.MaxEditionLength)]
    [TestCase(nameof(AdventureDetails.Levels), AdventureDetails.MaxLevelsLength)]
    public void AString_AtItsBound_IsValid_AndOnePast_IsRefusedByName(string field, int bound)
    {
        Assert.Multiple(() =>
        {
            Assert.That(With(field, new string('x', bound)).Validate(ThisYear), Is.Null);
            Assert.That(With(field, new string('x', bound + 1)).Validate(ThisYear), Is.EqualTo(field));
        });
    }

    [TestCase(AdventureDetails.MinYear, true)]
    [TestCase(AdventureDetails.MinYear - 1, false)]
    [TestCase(ThisYear + AdventureDetails.MaxYearsAhead, true)]
    [TestCase(ThisYear + AdventureDetails.MaxYearsAhead + 1, false)]
    [TestCase(0, false)]
    public void TheYear_IsBounded(int year, bool valid) =>
        Assert.That(new AdventureDetails { Year = year }.Validate(ThisYear),
            valid ? Is.Null : Is.EqualTo(nameof(AdventureDetails.Year)));

    private static AdventureDetails With(string field, string value) => field switch
    {
        nameof(AdventureDetails.GameSystem) => new AdventureDetails { GameSystem = value },
        nameof(AdventureDetails.Edition) => new AdventureDetails { Edition = value },
        _ => new AdventureDetails { Levels = value },
    };
}
