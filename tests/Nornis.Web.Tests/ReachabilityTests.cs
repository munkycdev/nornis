using System.Reflection;
using System.Text.RegularExpressions;
using Nornis.Web.Navigation;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Every member page has a door. On 2026-09-09 a campaign page was reachable only through a
/// GM-only settings panel and five routes had no inbound link at all — found by reading, not by
/// failing. This walks every <c>@page</c> route in the Web project and asserts each is linked
/// from the sidebar (<see cref="NavGroups"/>) or from at least one other page or shared
/// component, so the table cannot decay silently again.
///
/// Mechanical on purpose: it reads the razor source, the same way the sheet-isolation guard
/// reads the AI paths, and it self-checks that the directories it claims to scan exist.
/// </summary>
[TestFixture]
public class ReachabilityTests
{
    private static readonly Regex PageDirective = new(@"@page\s+""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// Routes that legitimately have no in-app door: the public surface (linked from the footer
    /// and the outside world), the entry points a person arrives at rather than navigates to,
    /// and the three redirect stubs kept for old bookmarks.
    /// </summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "/", "/Error", "/invite/{Code}", "/shelf/{Code}", "/welcome", "/about", "/features", "/changelog",
        "/privacy", "/terms", "/licenses", "/canon", "/graph", "/graph/{Focus}", "/costs", "/settings",
        // /extract is the older alias of /import; the page carries both.
        "/extract",
    };

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

    private static string Normalize(string route)
    {
        // Parameterised routes are compared by their static prefix: a link to
        // /artifacts/{some id} is a door to /artifacts/{Key}.
        var idx = route.IndexOf('{');
        return idx < 0 ? route.TrimEnd('/') : route[..idx].TrimEnd('/');
    }

    [Test]
    public void EveryMemberRoute_HasADoor()
    {
        var root = ComponentsRoot();
        var files = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Public{Path.DirectorySeparatorChar}"))
            .ToList();

        var routesByFile = files
            .Select(f => (File: f, Routes: PageDirective.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value).ToList()))
            .Where(x => x.Routes.Count > 0)
            .ToList();

        var navHrefs = NavGroups.All.SelectMany(g => g.Items).Select(i => i.Href).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undoored = new List<string>();
        foreach (var (file, routes) in routesByFile)
        {
            foreach (var route in routes)
            {
                if (Exempt.Contains(route))
                {
                    continue;
                }

                var prefix = Normalize(route);
                if (navHrefs.Contains(prefix))
                {
                    continue;
                }

                // A door is a link from a file other than the page itself: href="/x", Href="/x",
                // href="@($"/x/{...}")", NavigateTo("/x"), a constant string "/x/", or a call to
                // the Links method that spells /x — detail pages are linked through Links, so the
                // path is not in the razor at all.
                var pattern = new Regex(@"[""'(]" + Regex.Escape(prefix) + @"(?:[""'/?{]|$)", RegexOptions.IgnoreCase);
                var builders = LinkBuildersFor(prefix);
                var linkedFrom = files.Where(f => f != file).Where(f =>
                {
                    var text = File.ReadAllText(f);
                    return pattern.IsMatch(text) || builders.Any(b => text.Contains(b, StringComparison.Ordinal));
                }).ToList();
                if (linkedFrom.Count == 0)
                {
                    undoored.Add($"{route}  ({Path.GetRelativePath(root, file)})");
                }
            }
        }

        Assert.That(undoored, Is.Empty,
            "These routes have no link from the sidebar or any other page:\n  " + string.Join("\n  ", undoored));
    }

    /// <summary>
    /// The <c>Links.X(</c> calls that produce a path under <paramref name="prefix"/>, found by
    /// asking Links itself rather than by listing them here — so a new route in Links is a new
    /// door without this test knowing its name.
    /// </summary>
    private static IReadOnlyList<string> LinkBuildersFor(string prefix) =>
        typeof(Links).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string))
            .Where(m => m.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(string), typeof(Guid)]))
            .Where(m => ((string)m.Invoke(null, [null, Guid.Empty])!).StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            .Select(m => $"Links.{m.Name}(")
            .ToList();

    [Test]
    public void EverySidebarEntry_PointsAtARealRoute()
    {
        var root = ComponentsRoot();
        var routes = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => PageDirective.Matches(File.ReadAllText(f)).Select(m => Normalize(m.Groups[1].Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = NavGroups.All.SelectMany(g => g.Items).Where(i => !routes.Contains(i.Href)).Select(i => $"{i.Label} → {i.Href}").ToList();

        Assert.That(missing, Is.Empty, "Sidebar entries with no page behind them:\n  " + string.Join("\n  ", missing));
    }
}
