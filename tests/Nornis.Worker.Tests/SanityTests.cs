using NUnit.Framework;

namespace Nornis.Worker.Tests;

[TestFixture]
public class SanityTests
{
    [Test]
    public void Framework_IsConfiguredCorrectly()
    {
        Assert.That(true, Is.True);
    }
}
