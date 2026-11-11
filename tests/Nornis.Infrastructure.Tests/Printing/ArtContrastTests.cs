using System.Globalization;
using System.Xml.Linq;
using Nornis.Infrastructure.Printing;
using NUnit.Framework;
using SkiaSharp;

namespace Nornis.Infrastructure.Tests.Printing;

/// <summary>Feature 46: the scroll banner's author line takes the colour that reads on the art, with a shade when no colour does.</summary>
[TestFixture]
public class ArtContrastTests
{
    private const float Left = 0.15f;
    private const float Top = 0.9f;
    private const float Right = 0.85f;
    private const float Bottom = 0.96f;

    [Test]
    public void DarkArt_GetsWhiteType_WithNoShade()
    {
        var choice = ArtContrast.For(Solid(new SKColor(0x14, 0x1C, 0x2A)), Left, Top, Right, Bottom);

        Assert.That(choice, Is.EqualTo(new TextOnArt(ArtContrast.White, null)));
    }

    [Test]
    public void LightArt_GetsInk_WithNoShade()
    {
        var choice = ArtContrast.For(Solid(new SKColor(0xEE, 0xE6, 0xD2)), Left, Top, Right, Bottom);

        Assert.That(choice, Is.EqualTo(new TextOnArt(ArtContrast.Ink, null)));
    }

    /// <summary>Half bright, half dark: no single colour reads across it, so the line gets a shade.</summary>
    [Test]
    public void UnevenArt_GetsAShade_InTheOppositeColourToTheType()
    {
        var choice = ArtContrast.For(Halves(SKColors.White, new SKColor(0x10, 0x10, 0x10)), Left, Top, Right, Bottom);

        Assert.That(choice.Shade, Is.Not.Null);
        Assert.That(choice.Shade, Is.EqualTo(choice.Color == ArtContrast.White ? "#000000" : ArtContrast.White));
    }

    /// <summary>Only the strip is judged: dark art with a bright sky above the strip still gets white type and no shade.</summary>
    [Test]
    public void OnlyTheStripIsMeasured()
    {
        using var bitmap = new SKBitmap(400, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x10, 0x10, 0x10));
            using var sky = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 400, 500, sky);
        }

        var choice = ArtContrast.For(Encode(bitmap), Left, Top, Right, Bottom);

        Assert.That(choice, Is.EqualTo(new TextOnArt(ArtContrast.White, null)));
    }

    [Test]
    public void ArtThatCannotBeRead_GetsWhiteOnAShade()
    {
        Assert.That(ArtContrast.For([1, 2, 3], Left, Top, Right, Bottom), Is.EqualTo(new TextOnArt(ArtContrast.White, "#000000")));
    }

    [Test]
    public void TheContrastRatio_IsWcags()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ArtContrast.Contrast(ArtContrast.Luminance("#FFFFFF"), ArtContrast.Luminance("#000000")), Is.EqualTo(21).Within(0.01));
            Assert.That(ArtContrast.Contrast(ArtContrast.Luminance("#FFFFFF"), ArtContrast.Luminance("#777777")), Is.EqualTo(4.48).Within(0.01));
        });
    }

    /// <summary>Under a culture that writes "1,5", the SVG must still read "1.5", or the scroll and shade don't draw.</summary>
    [TestCase(504f, 120.5f)]
    [TestCase(80f, 30f)]
    public void TheScrollAndShade_AreWellFormedSvg_WhateverTheCulture(float width, float height)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            foreach (var svg in new[] { ScrollArt.Svg(width, height), ArtContrast.ShadeSvg(width, height, "#000000") })
            {
                var root = XDocument.Parse(svg).Root!;
                Assert.Multiple(() =>
                {
                    Assert.That(root.Attribute("width")!.Value, Is.EqualTo(width.ToString(CultureInfo.InvariantCulture)));
                    Assert.That(root.Attribute("viewBox")!.Value, Does.Not.Contain(","));
                    Assert.That(root.Descendants().SelectMany(e => e.Attributes()).Select(a => a.Value), Has.None.Matches(@"\d,\d"));
                });
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static byte[] Solid(SKColor color)
    {
        using var bitmap = new SKBitmap(400, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        return Encode(bitmap);
    }

    private static byte[] Halves(SKColor left, SKColor right)
    {
        using var bitmap = new SKBitmap(400, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(left);
            using var paint = new SKPaint { Color = right };
            canvas.DrawRect(200, 0, 200, 600, paint);
        }

        return Encode(bitmap);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
