using System.Globalization;
using System.Text;

namespace Nornis.Infrastructure.Printing;

/// <summary>
/// The parchment scroll the scroll banner cover sets its type on (feature 46), drawn as SVG at
/// whatever size the type needs: a sheet with gently waved edges between two rolled ends that
/// stand a little proud of it. It's vector, so it prints sharp, and it's drawn to the box rather
/// than stretched, so a three-line title gets a taller scroll with rolls of the same shape.
/// It's opaque, so the type on it never depends on the art's colours.
/// </summary>
internal static class ScrollArt
{
    /// <summary>How wide each rolled end is, in points.</summary>
    internal const float RollWidth = 20;

    /// <summary>How far the rolls stand above and below the sheet.</summary>
    internal const float RollOverhang = 7;

    /// <summary>The offset of the shadow that lifts the scroll off the art.</summary>
    internal const float ShadowOffset = 3;

    /// <summary>How far type must stay in from the box's left and right edges to clear the rolls.</summary>
    internal const float SideClearance = RollWidth + 14;

    /// <summary>How far type must stay in from the box's top and bottom to sit on the sheet, clear of its waved edge.</summary>
    internal const float EdgeClearance = RollOverhang + 12;

    private const string Sheet = "#F3EAD7";
    private const string SheetShade = "#E4D3AE";
    private const string RollLight = "#F7EED8";
    private const string RollMid = "#D9C9A6";
    private const string RollDark = "#A88A57";
    private const string Hollow = "#8C6F43";
    private const string Line = "#6E5636";

    /// <summary>The scroll as an SVG document exactly <paramref name="width"/> by <paramref name="height"/> points.</summary>
    internal static string Svg(float width, float height)
    {
        // The drawing keeps the shadow inside the box, so nothing is clipped at its edge.
        var w = Math.Max(width - ShadowOffset, 2 * RollWidth + 1);
        var h = Math.Max(height - ShadowOffset, 2 * RollOverhang + 1);
        var r = RollWidth;
        var o = RollOverhang;
        var cap = r * 0.3f;

        var sheet = SheetPath(r / 2, o, w - r / 2, h - o);
        var svg = new StringBuilder();
        svg.Append(Invariant($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">"""));
        svg.Append($"""
            <defs>
            <radialGradient id="sheet" cx="50%" cy="50%" r="70%">
            <stop offset="0%" stop-color="{Sheet}"/><stop offset="70%" stop-color="{Sheet}"/><stop offset="100%" stop-color="{SheetShade}"/>
            </radialGradient>
            <linearGradient id="roll" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stop-color="{RollDark}"/><stop offset="38%" stop-color="{RollLight}"/><stop offset="70%" stop-color="{RollMid}"/><stop offset="100%" stop-color="{RollDark}"/>
            </linearGradient>
            </defs>
            """);

        // The shadow: the whole outline, offset down and right.
        svg.Append(Invariant($"""<g transform="translate({ShadowOffset} {ShadowOffset})" fill="#000000" fill-opacity="0.35">"""));
        svg.Append($"""<path d="{sheet}"/>""");
        svg.Append(Roll(0, 0, r, h, cap, shadow: true));
        svg.Append(Roll(w - r, 0, r, h, cap, shadow: true));
        svg.Append("</g>");

        svg.Append($"""<path d="{sheet}" fill="url(#sheet)" stroke="{Line}" stroke-width="0.8"/>""");
        svg.Append(Roll(0, 0, r, h, cap, shadow: false));
        svg.Append(Roll(w - r, 0, r, h, cap, shadow: false));
        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>The sheet between the rolls: straight sides, its top and bottom a shallow S.</summary>
    private static string SheetPath(float left, float top, float right, float bottom)
    {
        var span = right - left;
        var wave = Math.Min(3.5f, (bottom - top) / 12);
        return Invariant(
            $"M {left} {top} C {left + span / 3} {top - wave} {left + 2 * span / 3} {top + wave} {right} {top} L {right} {bottom} C {left + 2 * span / 3} {bottom + wave} {left + span / 3} {bottom - wave} {left} {bottom} Z");
    }

    /// <summary>A rolled end seen from the front: a shaded cylinder whose top shows the curl of the sheet.</summary>
    private static string Roll(float x, float y, float width, float height, float cap, bool shadow)
    {
        var cx = x + width / 2;
        var rx = width / 2;
        var body = Invariant(
            $"M {x} {y + cap} L {x} {y + height - cap} A {rx} {cap} 0 0 0 {x + width} {y + height - cap} L {x + width} {y + cap} A {rx} {cap} 0 0 0 {x} {y + cap} Z");
        if (shadow)
        {
            return $"""<path d="{body}"/>""";
        }

        return Invariant($"""<path d="{body}" fill="url(#roll)" stroke="{Line}" stroke-width="0.8"/>""") +
            Invariant($"""<ellipse cx="{cx}" cy="{y + cap}" rx="{rx}" ry="{cap}" fill="{RollMid}" stroke="{Line}" stroke-width="0.8"/>""") +
            Invariant($"""<ellipse cx="{cx + rx * 0.12f}" cy="{y + cap}" rx="{rx * 0.55f}" ry="{cap * 0.55f}" fill="none" stroke="{Line}" stroke-width="0.6"/>""") +
            Invariant($"""<ellipse cx="{cx + rx * 0.2f}" cy="{y + cap}" rx="{rx * 0.22f}" ry="{cap * 0.22f}" fill="{Hollow}"/>""");
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
