using MudBlazor;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class LibraryDisplayTests
{
    [TestCase("Sourcebook", Icons.Material.Outlined.MenuBook)]
    [TestCase("Map", Icons.Material.Outlined.Map)]
    [TestCase("Handout", Icons.Material.Outlined.Description)]
    [TestCase("Something new", Icons.Material.Outlined.InsertDriveFile)]
    public void KindIcon_NamesEveryKind_AndFallsBackForTheRest(string kind, string expected)
    {
        Assert.That(LibraryDisplay.KindIcon(kind), Is.EqualTo(expected));
    }

    [Test]
    public void StatusLabel_IndexedSaysHowManyPassages()
    {
        Assert.That(LibraryDisplay.StatusLabel("Indexed", 42), Is.EqualTo("Indexed · 42 passages"));
        Assert.That(LibraryDisplay.StatusLabel("IndexFailed", 0), Is.EqualTo("Index failed"));
    }
}
