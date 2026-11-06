using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// The one resolver of a print's details (feature 32): the game system falls back to the
/// world's, type and publisher are Dave's fixed values, and the author is the creator.
/// </summary>
[TestFixture]
public class PrintDetailsTests
{
    [Test]
    public void TheFixedValues_AreDavesWording()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PrintDetails.NornisType, Is.EqualTo("Nornis adventure"));
            Assert.That(PrintDetails.NornisPublisher, Is.EqualTo("Nornis Press"));
            Assert.That(PrintDetails.NornisType, Has.Length.LessThanOrEqualTo(PrintDetails.MaxFixedLength));
            Assert.That(PrintDetails.NornisPublisher, Has.Length.LessThanOrEqualTo(PrintDetails.MaxFixedLength));
        });
    }

    [Test]
    public void Resolve_CarriesTheGmsDetails_AndFillsInTheFixedOnes()
    {
        var details = new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st", Levels = "1–3", Year = 2026 };

        var print = PrintDetails.Resolve("The Slime Spa", details, "Fate Accelerated", "kelda", AiUseKind.None);

        Assert.That(print, Is.EqualTo(new PrintDetails
        {
            Title = "The Slime Spa",
            GameSystem = "Shadowdark",
            Edition = "1st",
            Type = "Nornis adventure",
            Levels = "1–3",
            Publisher = "Nornis Press",
            Author = "kelda",
            Year = 2026,
            AiUse = AiUseKind.None,
            ResearchedOnWeb = false,
        }));
    }

    [Test]
    public void AnUnsetGameSystem_IsTheWorlds() =>
        Assert.That(PrintDetails.Resolve("The Slime Spa", new AdventureDetails(), "Fate Accelerated", "kelda", AiUseKind.None).GameSystem,
            Is.EqualTo("Fate Accelerated"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void NoGameSystemAnywhere_StaysUnset(string? worldGameSystem) =>
        Assert.That(PrintDetails.Resolve("The Slime Spa", new AdventureDetails(), worldGameSystem, "kelda", AiUseKind.None).GameSystem, Is.Null);

    [Test]
    public void UnsetDetails_StayUnset()
    {
        var print = PrintDetails.Resolve("The Slime Spa", new AdventureDetails(), null, "kelda", AiUseKind.None);

        Assert.Multiple(() =>
        {
            Assert.That(print.Edition, Is.Null);
            Assert.That(print.Levels, Is.Null);
            Assert.That(print.Year, Is.Null);
        });
    }

    [Test]
    public void Resolve_CarriesWhatTheAdventureRecordsAboutAi()
    {
        var print = PrintDetails.Resolve("The Slime Spa", new AdventureDetails(), null, "kelda", AiUseKind.TextAndArt);

        Assert.That(print.AiUse, Is.EqualTo(AiUseKind.TextAndArt));
    }
}
