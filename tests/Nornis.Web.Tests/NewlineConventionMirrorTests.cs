using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// A newline the GM types is a line break in the editor's preview (the Web's
/// <see cref="MarkdownRenderer"/>) and must be one in the printed booklet (Application's
/// <c>BookletModel</c>), across a deploy boundary no compiler spans. The booklet dropped them
/// once (feature 34's diagnosis of the Slurping Spa print); this keeps the two pipelines in step.
/// </summary>
[TestFixture]
public class NewlineConventionMirrorTests
{
    private const string Convention = ".UseSoftlineBreakAsHardlineBreak()";

    [Test]
    public void TheEditorsPreview_TurnsANewlineIntoABreak()
    {
        Assert.That(MarkdownRenderer.ToHtml("The vault door\nis cold.").Value, Does.Contain("<br"));
    }

    [Test]
    public void BothPipelines_DeclareTheSameNewlineConvention()
    {
        var root = RepoRoot();
        var web = File.ReadAllText(Path.Combine(root, "src", "Nornis.Web", "Services", "MarkdownRenderer.cs"));
        var booklet = File.ReadAllText(Path.Combine(root, "src", "Nornis.Application", "Prep", "BookletModel.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(web, Does.Contain(Convention), "the Web's MarkdownRenderer");
            Assert.That(booklet, Does.Contain(Convention), "Application's BookletModel");
        });
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
