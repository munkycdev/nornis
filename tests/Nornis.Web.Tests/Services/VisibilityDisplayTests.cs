using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class VisibilityDisplayTests
{
    [TestCase("PartyVisible", "Party visible")]
    [TestCase("GMOnly", "GM only")]
    [TestCase("Private", "Private")]
    public void Label_ReadsAsWords_NotAsTheIdentifier(string scope, string expected)
    {
        Assert.That(VisibilityDisplay.Label(scope), Is.EqualTo(expected));
    }

    [Test]
    public void Label_LeavesAnUnknownScopeAlone()
    {
        Assert.That(VisibilityDisplay.Label("Hidden"), Is.EqualTo("Hidden"));
    }

    [Test]
    public void EveryScope_HasALabelThatIsNotTheIdentifier()
    {
        foreach (var scope in VisibilityDisplay.Scopes.Where(s => s != "Private"))
        {
            Assert.That(VisibilityDisplay.Label(scope), Is.Not.EqualTo(scope), scope);
        }
    }
}
