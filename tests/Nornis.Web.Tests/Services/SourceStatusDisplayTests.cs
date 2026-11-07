using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class SourceStatusDisplayTests
{
    [TestCase("Processed", false)]
    [TestCase("Failed", true)]
    [TestCase("Queued", true)]
    [TestCase("Draft", true)]
    public void IsWorthShowing_IsSilentOnlyForProcessed(string status, bool expected)
    {
        Assert.That(SourceStatusDisplay.IsWorthShowing(status), Is.EqualTo(expected));
    }
}
