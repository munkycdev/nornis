using Nornis.Application.Common;
using NUnit.Framework;

namespace Nornis.Application.Tests.Common;

[TestFixture]
public class StringExtensionsTests
{
    [TestCase("image/png", "image/png")]
    [TestCase("text/plain\r\nINFO forged entry", "text/plainINFO forged entry")]
    [TestCase("a\nb\rc", "abc")]
    [TestCase("", "")]
    public void ForLog_removes_line_breaks_and_keeps_the_rest(string value, string expected)
    {
        Assert.That(value.ForLog(), Is.EqualTo(expected));
    }

    [Test]
    public void ForLog_leaves_null_as_null()
    {
        Assert.That(((string?)null).ForLog(), Is.Null);
    }
}
