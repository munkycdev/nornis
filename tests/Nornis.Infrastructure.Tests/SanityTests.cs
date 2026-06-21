using NUnit.Framework;

namespace Nornis.Infrastructure.Tests;

[TestFixture]
public class SanityTests
{
    [Test]
    public void Framework_IsConfiguredCorrectly()
    {
        Assert.That(true, Is.True);
    }
}
