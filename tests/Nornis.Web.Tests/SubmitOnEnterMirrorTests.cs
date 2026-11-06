using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Which keypress sends a Talk it through message is decided twice: nornis-compose.js stops the
/// newline in the browser, and <c>AdventureConversationPanel.SendsOnEnter</c> sends. No compiler
/// spans the two, and if they drifted, Enter would either send and also type a newline, or type
/// nothing and send nothing. This reads both and requires them to test the same key properties.
/// </summary>
[TestFixture]
public class SubmitOnEnterMirrorTests
{
    [Test]
    public void TheBrowserAndThePanel_DecideEnterOnTheSameKeys()
    {
        var root = RepoRoot();
        var js = File.ReadAllText(Path.Combine(root, "src", "Nornis.Web", "wwwroot", "js", "nornis-compose.js"));
        var razor = File.ReadAllText(Path.Combine(root, "src", "Nornis.Web", "Components", "Shared", "AdventureConversationPanel.razor"));

        var jsBody = Regex.Match(js, @"sendsOnEnter\(e\)\s*\{(?<body>.*?)\}", RegexOptions.Singleline);
        var csBody = Regex.Match(razor, @"bool SendsOnEnter\(KeyboardEventArgs e\) =>(?<body>.*?);", RegexOptions.Singleline);
        Assert.That(jsBody.Success, Is.True, "no sendsOnEnter(e) in nornis-compose.js");
        Assert.That(csBody.Success, Is.True, "no SendsOnEnter(KeyboardEventArgs e) in AdventureConversationPanel.razor");

        Assert.Multiple(() =>
        {
            Assert.That(Clauses(csBody.Groups["body"].Value), Is.EquivalentTo(Clauses(jsBody.Groups["body"].Value)));
            Assert.That(Clauses(jsBody.Groups["body"].Value), Does.Contain("!shiftkey"), "Shift+Enter must stay a newline");
        });
    }

    /// <summary>Each test on the event, as "property" or "!property", ignoring case and the key's quotes.</summary>
    private static IReadOnlyList<string> Clauses(string body) =>
        Regex.Matches(body, @"(!?)e\.(\w+)(?:\s*===?\s*['""](\w+)['""])?")
            .Select(m => $"{m.Groups[1].Value}{m.Groups[2].Value}{(m.Groups[3].Success ? "=" + m.Groups[3].Value : "")}".ToLowerInvariant())
            .ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
