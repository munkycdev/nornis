using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The draft stream's event names exist twice, in the API's responses and the Web's reader, with
/// no project between them. An event the Web doesn't know is skipped, so a renamed <c>done</c>
/// would read as a stream that never finished. This reads both sources and requires the same set.
/// </summary>
[TestFixture]
public class DraftStreamEventNamesMirrorTests
{
    [Test]
    public void TheWebReadsExactlyTheEventsTheApiWrites()
    {
        var api = Names(Path.Combine("src", "Nornis.Api", "Contracts", "Responses", "PrepResponses.cs"));
        var web = Names(Path.Combine("src", "Nornis.Web", "ApiClient", "DraftStream.cs"));

        Assert.That(api, Is.Not.Empty, "found no event names in the API's DraftStreamEventNames");
        Assert.That(web, Is.EquivalentTo(api));
    }

    private static Dictionary<string, string> Names(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        var block = Regex.Match(source, @"class DraftStreamEventNames\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
        Assert.That(block.Success, Is.True, $"no DraftStreamEventNames in {relativePath}");
        return Regex.Matches(block.Groups["body"].Value, @"public const string (\w+) = ""([^""]*)"";")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
