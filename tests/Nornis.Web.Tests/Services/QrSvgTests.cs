using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class QrSvgTests
{
    [Test]
    public void For_ReturnsInlineSvg_ScaledByViewBox()
    {
        var svg = QrSvg.For("https://nornis.app/shelf/abc123");

        Assert.Multiple(() =>
        {
            Assert.That(svg, Does.StartWith("<svg"));
            Assert.That(svg, Does.Contain("viewBox="), "sized by the container, not in pixels");
            Assert.That(svg, Does.Contain("#000000").And.Contain("#FFFFFF"), "black on white for the scanner");
        });
    }

    [Test]
    public void For_DifferentLinks_DifferentCodes()
    {
        Assert.That(QrSvg.For("https://nornis.app/shelf/one"), Is.Not.EqualTo(QrSvg.For("https://nornis.app/shelf/two")));
    }
}
