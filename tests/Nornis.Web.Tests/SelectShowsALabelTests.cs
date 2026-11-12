using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// A pick list shows a label, not the value the API stores (3.32). "GMOnly", "SessionNote" and
/// "PartyVisible" reached the screen in a dozen selects because the item's text was the loop
/// variable itself. This reads every component's source and fails on a
/// <c>MudSelectItem</c> whose text is exactly its own value.
///
/// Mechanical on purpose, like <see cref="ReachabilityTests"/>: a select that prints its raw
/// value compiles, renders and passes every other test.
/// </summary>
[TestFixture]
public class SelectShowsALabelTests
{
    /// <summary>
    /// Selects whose stored value is already the word a reader would use: one plain word, in
    /// sentence case. A value that grows a second word (or a prefix) comes off this list and
    /// gets a label.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ValueIsTheLabel = new Dictionary<string, string>
    {
        ["CampaignDetail.razor"] = "status",
        ["LibraryDocumentDetail.razor"] = "category",
    };

    private static readonly Regex RawItem = new(
        @"<MudSelectItem\b[^>]*\bValue=""@\(?(?<value>[A-Za-z_][\w.]*)\)?""[^>]*>\s*@\(?(?<text>[A-Za-z_][\w.]*)\)?\s*</MudSelectItem>",
        RegexOptions.Compiled);

    private static string ComponentsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Web")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test binary.");
        var root = Path.Combine(dir!.FullName, "src", "Nornis.Web", "Components");
        Assert.That(Directory.Exists(root), Is.True, $"{root} does not exist, so the scan would prove nothing.");
        return root;
    }

    private static IEnumerable<(string File, string Value)> RawItems()
    {
        foreach (var path in Directory.EnumerateFiles(ComponentsRoot(), "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match match in RawItem.Matches(File.ReadAllText(path)))
            {
                if (match.Groups["value"].Value == match.Groups["text"].Value)
                {
                    yield return (Path.GetFileName(path), match.Groups["value"].Value);
                }
            }
        }
    }

    [Test]
    public void NoSelect_ShowsItsStoredValueAsItsText()
    {
        var offenders = RawItems()
            .Where(item => !(ValueIsTheLabel.TryGetValue(item.File, out var allowed) && allowed == item.Value))
            .Select(item => $"{item.File}: @{item.Value}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "These selects print the stored value. Give each a label (VisibilityDisplay.Label, SourceTypeDisplay.Label, ...).");
    }

    [Test]
    public void TheScan_FindsTheSelectsItExcuses()
    {
        // A scan that matches nothing passes for the wrong reason. The two excused selects are
        // the proof the pattern still matches the way a raw item is written.
        var found = RawItems().Select(item => (item.File, item.Value)).ToList();

        foreach (var (file, value) in ValueIsTheLabel)
        {
            Assert.That(found, Does.Contain((file, value)), $"{file} no longer has a raw '{value}' item; drop it from the list");
        }
    }
}
