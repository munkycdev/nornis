using System.Globalization;
using SkiaSharp;

namespace Nornis.Infrastructure.Printing;

/// <summary>What colour a line of type set straight on the art takes, and whether it needs a shade behind it.</summary>
/// <param name="Color">The type's colour: white or the book's ink.</param>
/// <param name="Shade">Null when the art behind the line is even enough to read on; otherwise the colour of a soft translucent shade to lay under the line (black under white type, white under ink).</param>
internal sealed record TextOnArt(string Color, string? Shade);

/// <summary>
/// Picks the colour for type set on the art (feature 46, which amends feature 38's "type never
/// sits on the art" for the scroll banner's author line). It measures the strip of the very
/// image the page prints, so what it judges is what the reader sees:
///
/// - the type is white or ink, whichever has the higher WCAG contrast against the strip's mean
///   luminance;
/// - when more than <see cref="MaxPoorShare"/> of the strip falls under <see cref="MinContrast"/>
///   against that colour (bright sky over dark rock, say), the line gets a soft shade, because no
///   single colour reads everywhere on it.
/// </summary>
internal static class ArtContrast
{
    /// <summary>WCAG's contrast floor for large type: the author line is 13 pt semibold.</summary>
    internal const double MinContrast = 3.0;

    /// <summary>The share of the strip allowed under <see cref="MinContrast"/> before the line gets a shade.</summary>
    internal const double MaxPoorShare = 0.1;

    /// <summary>The most pixels measured in a strip: plenty to judge brightness, cheap at print time.</summary>
    internal const int MaxSamples = 20_000;

    internal const string White = "#FFFFFF";
    internal const string Ink = PrintColors.Ink;

    private static readonly double WhiteLuminance = 1.0;
    private static readonly double InkLuminance = Luminance(Ink);

    /// <summary>How the shade reads at its centre; it fades to nothing at its edge.</summary>
    private const double ShadeOpacity = 0.55;

    /// <summary>
    /// The colour for type over the strip of <paramref name="art"/> between the given fractions of
    /// its width and height (0 is the left or top). Art that can't be read gets white on a shade,
    /// which reads on anything.
    /// </summary>
    internal static TextOnArt For(byte[] art, float left, float top, float right, float bottom)
    {
        using var bitmap = CoverArtImage.Decode(art);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return new TextOnArt(White, "#000000");
        }

        var x0 = Math.Clamp((int)(left * bitmap.Width), 0, bitmap.Width - 1);
        var x1 = Math.Clamp((int)Math.Ceiling(right * bitmap.Width), x0 + 1, bitmap.Width);
        var y0 = Math.Clamp((int)(top * bitmap.Height), 0, bitmap.Height - 1);
        var y1 = Math.Clamp((int)Math.Ceiling(bottom * bitmap.Height), y0 + 1, bitmap.Height);
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)(x1 - x0) * (y1 - y0) / MaxSamples)));

        var luminances = new List<double>();
        for (var y = y0; y < y1; y += step)
        {
            for (var x = x0; x < x1; x += step)
            {
                var pixel = bitmap.GetPixel(x, y);
                luminances.Add(Luminance(pixel.Red, pixel.Green, pixel.Blue));
            }
        }

        return Judge(luminances);
    }

    /// <summary>The choice for a strip whose pixels have these relative luminances.</summary>
    internal static TextOnArt Judge(IReadOnlyCollection<double> luminances)
    {
        if (luminances.Count == 0)
        {
            return new TextOnArt(White, "#000000");
        }

        var mean = luminances.Average();
        var white = Contrast(WhiteLuminance, mean) >= Contrast(InkLuminance, mean);
        var type = white ? WhiteLuminance : InkLuminance;
        var poor = luminances.Count(l => Contrast(type, l) < MinContrast) / (double)luminances.Count;
        return new TextOnArt(white ? White : Ink, poor > MaxPoorShare ? (white ? "#000000" : White) : null);
    }

    /// <summary>The WCAG contrast ratio between two relative luminances.</summary>
    internal static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    /// <summary>WCAG relative luminance of an sRGB colour.</summary>
    internal static double Luminance(byte red, byte green, byte blue) =>
        0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);

    /// <summary>WCAG relative luminance of a #RRGGBB colour.</summary>
    internal static double Luminance(string hex) => Luminance(
        byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>How many nested ellipses build the shade's soft edge.</summary>
    private const int ShadeRings = 12;

    /// <summary>
    /// The shade as SVG: nested ellipses of <paramref name="color"/>, each a little smaller and each
    /// faint, so they build to <see cref="ShadeOpacity"/> at the centre and fade out at the edge. It
    /// reads as a softening of the art rather than a box. (Skia's SVG drops gradient stop opacity,
    /// so a radial gradient to transparent draws as a solid oval.)
    /// </summary>
    internal static string ShadeSvg(float width, float height, string color)
    {
        // Each ring's opacity, so that all of them stacked reach ShadeOpacity.
        var ring = 1 - Math.Pow(1 - ShadeOpacity, 1.0 / ShadeRings);
        var svg = new System.Text.StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">"""));
        for (var i = 0; i < ShadeRings; i++)
        {
            var scale = 1 - 0.5f * i / ShadeRings;
            svg.Append(string.Create(CultureInfo.InvariantCulture,
                $"""<ellipse cx="{width / 2}" cy="{height / 2}" rx="{width / 2 * scale}" ry="{height / 2 * scale}" fill="{color}" fill-opacity="{ring:0.###}"/>"""));
        }

        return svg.Append("</svg>").ToString();
    }
}
