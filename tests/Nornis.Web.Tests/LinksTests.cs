using System.Text.RegularExpressions;
using Nornis.Web.Navigation;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Detail pages are addressed by slug, with the id as the fallback for a row the backfill has
/// not reached. The first half pins the helper; the second walks every razor file the way
/// <see cref="ReachabilityTests"/> does and fails if a page has gone back to spelling a detail
/// path by hand with an id in it — which is how a hundred GUID links accumulated in the first
/// place, one reasonable line at a time.
/// </summary>
[TestFixture]
public class LinksTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Test]
    public void SlugWins_IdIsTheFallback()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Links.Artifact("the-ashen-king", Id), Is.EqualTo("/codex/the-ashen-king"));
            Assert.That(Links.Artifact(null, Id), Is.EqualTo($"/codex/{Id}"));
            Assert.That(Links.Artifact("", Id), Is.EqualTo($"/codex/{Id}"), "empty is unset, not a slug");
            Assert.That(Links.Campaign("ash-and-salt", Id), Is.EqualTo("/campaigns/ash-and-salt"));
            Assert.That(Links.Character("mira-voss", Id), Is.EqualTo("/characters/mira-voss"));
            Assert.That(Links.Source("session-12", Id), Is.EqualTo("/sources/session-12"));
            Assert.That(Links.LibraryDocument("players-guide", Id), Is.EqualTo("/library/players-guide"));
            Assert.That(Links.InkCapture("session-12", Id), Is.EqualTo("/capture/ink/session-12"));
            Assert.That(Links.PublicArtifact("black-harbor", "the-ashen-king", Id), Is.EqualTo("/w/black-harbor/codex/the-ashen-king"));
            Assert.That(Links.PublicCampaign("black-harbor", null, Id), Is.EqualTo($"/w/black-harbor/campaigns/{Id}"));
            Assert.That(Links.PublicSource("black-harbor", "session-12", Id), Is.EqualTo("/w/black-harbor/sources/session-12"));
        });
    }

    [Test]
    public void Canonical_MeansTheBarAlreadyShowsTheSlug()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Links.IsCanonical("the-ashen-king", "the-ashen-king"), Is.True);
            Assert.That(Links.IsCanonical(Id.ToString(), "the-ashen-king"), Is.False, "an old id link gets rewritten");
            Assert.That(Links.IsCanonical("The-Ashen-King", "the-ashen-king"), Is.False, "a pasted slug in the wrong case gets rewritten");
            Assert.That(Links.IsCanonical(Id.ToString(), null), Is.True, "nothing to rewrite to until the backfill runs");
        });
    }

    private static readonly Regex HandSpelledDetailPath = new(
        @"(/codex|/artifacts|/sources|/campaigns|/characters|/library|/capture/ink)/\{[A-Za-z_.!()?]*Id[A-Za-z_.!()?]*\}",
        RegexOptions.Compiled);

    [Test]
    public void NoPage_SpellsADetailPathWithAnId()
    {
        var root = ComponentsRoot();
        var offenders = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, i) => (file, line, i))
                .Where(x => !x.line.TrimStart().StartsWith("@page", StringComparison.Ordinal))
                .Where(x => HandSpelledDetailPath.IsMatch(x.line))
                .Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.i + 1}: {x.line.Trim()}"))
            .ToList();

        Assert.That(offenders, Is.Empty, "build the link through Links so the slug is used:\n" + string.Join('\n', offenders));
    }

    private static string ComponentsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Web")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test binary.");
        var root = Path.Combine(dir!.FullName, "src", "Nornis.Web", "Components");
        Assert.That(Directory.Exists(root), Is.True, $"{root} does not exist — the scan would prove nothing.");
        return root;
    }
}
