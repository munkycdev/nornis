using Nornis.Application.Art;
using Nornis.Application.Tests.Fakes;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

[TestFixture]
public class ImageFileTests
{
    [Test]
    public void APng_IsReadFromItsHeader()
    {
        Assert.That(ImageFile.Read(TestImages.Png(1024, 1536)), Is.EqualTo(new ImageFileInfo("image/png", ".png", 1024, 1536)));
    }

    [Test]
    public void AJpeg_IsReadFromItsFirstStartOfFrame_PastOtherSegments()
    {
        Assert.That(ImageFile.Read(TestImages.Jpeg(1200, 1800)), Is.EqualTo(new ImageFileInfo("image/jpeg", ".jpg", 1200, 1800)));
    }

    [TestCase(new byte[] { })]
    [TestCase(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [TestCase(new byte[] { 0xFF, 0xD8, 0xFF })]
    [TestCase(new byte[] { 0xFF, 0xD8, 0x00, 0x00, 0x00, 0x00 })]
    public void AnythingElse_IsNotAnImage(byte[] bytes)
    {
        Assert.That(ImageFile.Read(bytes), Is.Null);
    }

    [Test]
    public void APngClaimingNoPixels_IsNotAnImage()
    {
        Assert.That(ImageFile.Read(TestImages.Png(0, 1536)), Is.Null);
    }
}
