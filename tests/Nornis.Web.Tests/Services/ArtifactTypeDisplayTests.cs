using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class ArtifactTypeDisplayTests
{
    [TestCase("Active", false)]
    [TestCase("Dormant", true)]
    [TestCase("Resolved", true)]
    [TestCase("Archived", true)]
    public void IsWorthShowing_IsSilentOnlyForActive(string status, bool expected)
    {
        Assert.That(ArtifactTypeDisplay.IsWorthShowing(status), Is.EqualTo(expected));
    }
}
