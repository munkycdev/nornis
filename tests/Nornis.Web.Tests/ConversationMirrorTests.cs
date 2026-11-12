using System.Text.RegularExpressions;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The side conversation's names and bound, which the Web mirrors because it can't reference
/// Domain or Application: the message roles it tells apart, the Put in section modes it sends
/// (the API refuses any other with 400), and the message length its box allows.
/// </summary>
[TestFixture]
public class ConversationMirrorTests
{
    [Test]
    public void TheRoles_AreTheDomainsConversationRoles() =>
        Assert.That(new[] { ConversationMessageDto.UserRole, ConversationMessageDto.AssistantRole },
            Is.EqualTo(EnumMembers("src/Nornis.Domain/Enums/ConversationRole.cs", "ConversationRole")));

    [Test]
    public void TheEditOutcomes_AreTheDomainsDraftEditOutcomes() =>
        Assert.That(new[] { ConversationMessageDto.AppliedOutcome, ConversationMessageDto.DidNotFitOutcome, ConversationMessageDto.NotAppliedOutcome },
            Is.EqualTo(EnumMembers("src/Nornis.Domain/Enums/DraftEditOutcome.cs", "DraftEditOutcome")));

    [Test]
    public void TheModes_AreTheApplicationsPutInSectionModes() =>
        Assert.That(new[] { PutInSectionRequest.Replace, PutInSectionRequest.Append },
            Is.EqualTo(EnumMembers("src/Nornis.Application/Models/PrepModels.cs", "PutInSectionMode")));

    [Test]
    public void TheScopes_AreTheApplicationsConversationScopes() =>
        Assert.That(new[] { SendConversationMessageRequest.SectionScope, SendConversationMessageRequest.DraftScope, SendConversationMessageRequest.BookScope },
            Is.EqualTo(EnumMembers("src/Nornis.Application/Models/PrepModels.cs", "ConversationScope")));

    [Test]
    public void TheSplitModes_AreTheApplicationsDraftSplitModes() =>
        Assert.That(new[] { SplitDraftRequest.Replace, SplitDraftRequest.Append },
            Is.EqualTo(EnumMembers("src/Nornis.Application/Models/PrepModels.cs", "DraftSplitMode")));

    [Test]
    public void TheMessageBox_AllowsWhatTheApiAllows()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Application", "Configuration", "PrepOptions.cs"));
        var match = Regex.Match(source, @"MaxConversationMessageChars\s*\{\s*get;\s*set;\s*\}\s*=\s*(?<n>[\d_]+);");

        Assert.That(match.Success, Is.True, "no MaxConversationMessageChars default in PrepOptions.cs");
        Assert.That(AdventureConversationPanel.MaxMessageChars, Is.EqualTo(int.Parse(match.Groups["n"].Value.Replace("_", ""))));
    }

    private static List<string> EnumMembers(string relativePath, string enumName)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        var start = source.IndexOf($"enum {enumName}", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no enum {enumName} in {relativePath}");
        var body = source[start..source.IndexOf('}', start)];
        var members = Regex.Matches(body, @"^\s*(\w+),\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.That(members, Is.Not.Empty, $"found no members in {enumName}");
        return members;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
