using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Every member has exactly one linked player from the moment the membership exists (feature
/// 25). The database holds half of that with a filtered unique index; this holds the other
/// half, that a membership is never built anywhere but <c>WorldMembership.Create</c>, which
/// attaches the player. A service that constructed a <c>WorldMember</c> by hand would compile,
/// run, and leave a member the Party page cannot show and claiming cannot target.
///
/// Mechanical, like <c>CharacterSheetIsolationTests</c>: the defect is a line someone writes,
/// and no assertion about behaviour names it. Sabotaged by adding a bare construction to
/// <c>WorldInviteService</c>; failed naming the file.
/// </summary>
[TestFixture]
public class MemberPlayerScanTests
{
    private static readonly Regex Construction = new(@"new\s+WorldMember\s*(\{|\()", RegexOptions.Compiled);

    private static readonly string Factory = Path.Combine("src", "Nornis.Application", "Services", "WorldMembership.cs");

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Application")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test binary.");
        return dir!;
    }

    [Test]
    public void EveryMembership_IsBuiltByTheFactory()
    {
        var root = RepositoryRoot();
        var src = Path.Combine(root.FullName, "src");
        Assert.That(Directory.Exists(src), Is.True, $"{src} does not exist — the scan would prove nothing.");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => Construction.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root.FullName, f))
            .Where(f => !string.Equals(f, Factory, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "A membership built outside WorldMembership.Create has no player:\n  " + string.Join("\n  ", offenders));
    }

    [Test]
    public void TheFactory_IsWhereItIsClaimedToBe()
    {
        // The scan above passes vacuously if the factory moved and every construction went
        // with it — so pin that the one allowed file exists and does construct.
        var path = Path.Combine(RepositoryRoot().FullName, Factory);
        Assert.That(File.Exists(path), Is.True, $"{Factory} is missing");
        Assert.That(Construction.IsMatch(File.ReadAllText(path)), Is.True, "the factory no longer constructs a WorldMember");
    }
}
