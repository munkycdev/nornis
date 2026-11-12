using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class SourceStatusDisplayTests
{
    [TestCase("Processed", false)]
    [TestCase("Failed", true)]
    [TestCase("Queued", true)]
    [TestCase("Draft", true)]
    public void IsWorthShowing_IsSilentOnlyForProcessed(string status, bool expected)
    {
        Assert.That(SourceStatusDisplay.IsWorthShowing(status), Is.EqualTo(expected));
    }

    [TestCase("Ready", "Waiting")]
    [TestCase("Queued", "Waiting")]
    [TestCase("Processing", "Reading")]
    [TestCase("Processed", "In the record")]
    [TestCase("Draft", "Draft")]
    [TestCase("Failed", "Failed")]
    public void AStatus_IsSaidInTheWordsTheButtonsUse(string status, string expected)
    {
        Assert.That(SourceStatusDisplay.Label(status), Is.EqualTo(expected));
    }

    [Test]
    public void TheTwoWaitingStates_AreOneChip_AndItFindsBoth()
    {
        // Ready and Queued showed as two chips nobody could tell apart (3.32). With one chip,
        // a filter that matched the raw status would hide every Ready source under "Waiting".
        Assert.Multiple(() =>
        {
            Assert.That(SourceStatusDisplay.FilterOrder.Select(SourceStatusDisplay.Label), Is.Unique);
            Assert.That(SourceStatusDisplay.Matches("Queued", "Ready"), Is.True);
            Assert.That(SourceStatusDisplay.Matches("Queued", "Queued"), Is.True);
            Assert.That(SourceStatusDisplay.Matches("Queued", "Processing"), Is.False);
        });
    }

    [Test]
    public void EveryStatus_HasAChipThatFindsIt()
    {
        string[] all = ["Draft", "Ready", "Queued", "Processing", "Processed", "Failed"];

        Assert.That(all.Where(s => !SourceStatusDisplay.FilterOrder.Any(f => SourceStatusDisplay.Matches(f, s))), Is.Empty);
    }
}
