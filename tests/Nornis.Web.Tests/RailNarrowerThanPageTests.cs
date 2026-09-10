using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Property 1 of feature 24: the rail never widens what a page may show. It is enforced by
/// construction — each migrated page builds its rail model from the detail it already loaded at
/// the reader's role, and never fetches for the rail — and this test holds the construction in
/// place. It reads each migrated page's source, finds the rail-model members, and asserts they
/// read from the page's own state fields and make no API call of their own. A rail that fetched
/// would be a second read path with its own visibility to get wrong; forbidding the fetch
/// forbids the class of bug.
///
/// Mechanical on purpose, like <see cref="ReachabilityTests"/>: a page can be given a rail that
/// calls the API and still compile, render and look right.
/// </summary>
[TestFixture]
public class RailNarrowerThanPageTests
{
    /// <summary>The migrated pages and the state fields their rail models may read.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Pages = new Dictionary<string, string[]>
    {
        ["ArtifactDetail.razor"] = ["_detail"],
        ["SourceDetail.razor"] = ["_source", "_knowledge", "_attachments"],
        ["CampaignDetail.razor"] = ["_detail", "CampaignId"],
        ["CharacterDetail.razor"] = ["_dossier"],
        ["LibraryDocumentDetail.razor"] = ["_document"],
    };

    private static readonly Regex Member = new(
        @"private IReadOnlyList<ContextRail\.(?:RailRow|RailLink)> (RailRows|RailRelated)\b(?<body>.*?)(?=\n    private |\n    protected |\n    public |\n\}\s*$)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex StateField = new(@"(?<![.\w])(_[a-z][A-Za-z0-9]*|CampaignId)\b", RegexOptions.Compiled);

    private static string PagesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Web")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test binary.");
        var root = Path.Combine(dir!.FullName, "src", "Nornis.Web", "Components", "Pages");
        Assert.That(Directory.Exists(root), Is.True, $"{root} does not exist — the scan would prove nothing.");
        return root;
    }

    public static IEnumerable<string> PageNames => Pages.Keys;

    [TestCaseSource(nameof(PageNames))]
    public void MigratedPage_BuildsItsRailFromWhatItAlreadyLoaded(string page)
    {
        var source = File.ReadAllText(Path.Combine(PagesRoot(), page)).Replace("\r\n", "\n");

        Assert.That(source, Does.Contain("<EntityPage>"), $"{page} is not on the template");
        Assert.That(source, Does.Contain("<ContextRail "), $"{page} has no rail");

        var members = Member.Matches(source);
        Assert.That(members.Select(m => m.Groups[1].Value), Is.EquivalentTo(["RailRows", "RailRelated"]),
            $"{page} must define both rail members");

        var allowed = Pages[page];
        foreach (Match member in members)
        {
            var name = member.Groups[1].Value;
            var body = member.Groups["body"].Value;
            Assert.That(body, Does.Not.Contain("Api."), $"{page}.{name} fetches for the rail");
            Assert.That(body, Does.Not.Contain("await "), $"{page}.{name} awaits something");

            var fields = StateField.Matches(body).Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.That(fields, Is.SubsetOf(allowed), $"{page}.{name} reads state the page's detail did not load");
        }
    }
}
