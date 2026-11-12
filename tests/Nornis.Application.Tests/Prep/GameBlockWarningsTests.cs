using Nornis.Application.Prep;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>The warnings under a section's editor (feature 35, Requirement 4).</summary>
[TestFixture]
public class GameBlockWarningsTests
{
    private static string Fence(string info, string body) => $"```{info}\n{body}\n```\n\n";

    [Test]
    public void AGoodSection_HasNoWarnings()
    {
        var markdown = "Intro.\n\n" + Fence("statblock", "# Voss\nAC: 15") + Fence("item", "# Rope\nLength: 50 ft");

        Assert.That(GameBlockWarnings.For(markdown), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("No blocks at all.")]
    public void NothingToWarnAbout_IsNoWarnings(string? markdown) =>
        Assert.That(GameBlockWarnings.For(markdown), Is.Empty);

    [Test]
    public void AnUnnamedBlock_IsNamedByItsPlace_AmongItsOwnKind()
    {
        var markdown = Fence("statblock", "# Voss\nAC: 15") + Fence("item", "Weight: 1 lb") + Fence("statblock", "AC: 12");

        Assert.That(GameBlockWarnings.For(markdown), Is.EqualTo((string[])
        [
            "The first item block has no name, so Add to the record can't file it on anything. Start it with \"# Name\".",
            "The second stat block has no name, so Add to the record can't file it on anyone. Start it with \"# Name\".",
        ]));
    }

    [Test]
    public void AnEmptyBlock_IsEmpty_NotUnnamed() =>
        Assert.That(GameBlockWarnings.For(Fence("item", "")), Is.EqualTo((string[])["The first item block is empty."]));

    [Test]
    public void ALongBlock_WarnsExactlyWhenTheExportCutsIt()
    {
        var entries = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"Trait {i:D3}: {new string('x', 20)}"));
        var block = "# Hydra\n" + entries;

        Assert.Multiple(() =>
        {
            Assert.That(GameBlocks.ExportLineFor(GameBlocks.InMarkdown(Fence("statblock", block)).Single(), "Shadowdark")!.WasCut, Is.True);
            Assert.That(GameBlockWarnings.For(Fence("statblock", block)), Is.EqualTo((string[])
                ["Hydra's stat block is too long to file as one fact; Add to the record will cut it short."]));
        });
    }

    [Test]
    public void PastTheTenth_TheBlockIsNumbered()
    {
        var markdown = string.Concat(Enumerable.Range(1, 11).Select(_ => Fence("statblock", "AC: 12")));

        Assert.That(GameBlockWarnings.For(markdown)[10], Does.StartWith("Stat block 11 has no name"));
    }
}
