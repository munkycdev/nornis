using System.Globalization;
using System.Text;

namespace Nornis.Infrastructure.Printing;

/// <summary>
/// The gilded frame the scroll banner cover sets its title on (feature 46), drawn as SVG at
/// whatever size the type needs: an ivory panel in a bevelled gold frame with scooped corners,
/// gilt C-scrolls curling from its sides and along its top, a fan crest above, a small shell below and a rosette in each corner. It's vector, so it
/// prints sharp, and it's drawn to the box rather than stretched, so a three-line title gets a
/// taller frame with ornaments of the same size. The panel is opaque, so the type on it never
/// depends on the art's colours.
/// </summary>
internal static class GildedFrameArt
{
    /// <summary>The room each side's C-scroll takes outside the frame.</summary>
    internal const float SideOrnament = 22;

    /// <summary>The room the crest takes above the frame.</summary>
    internal const float Crest = 18;

    /// <summary>The room the shell takes below the frame.</summary>
    internal const float Shell = 12;

    /// <summary>The gold band's width.</summary>
    internal const float Band = 9;

    /// <summary>The offset of the shadow that lifts the frame off the art.</summary>
    internal const float ShadowOffset = 3;

    /// <summary>How far type must stay in from the box's left and right edges to sit on the panel.</summary>
    internal const float SideClearance = SideOrnament + Band + 16;

    /// <summary>How far type must stay below the box's top to sit on the panel.</summary>
    internal const float TopClearance = Crest + Band + 10;

    /// <summary>How far type must stay above the box's bottom to sit on the panel.</summary>
    internal const float BottomClearance = Shell + Band + 10;

    /// <summary>The scooped corners' radius.</summary>
    private const float Corner = 15;

    private const string Panel = "#F8F1E0";
    private const string PanelShade = "#EADBB8";
    private const string GoldLine = "#B8892E";
    private const string GoldWire = "#D6A948";
    private const string GoldLight = "#FBE7A6";

    /// <summary>The corner rosettes' radius.</summary>
    private const float RosetteRadius = 6.5f;

    /// <summary>The C-scrolls' gold width.</summary>
    private const float ScrollWeight = 3.2f;
    private const string Edge = "#5E410F";

    /// <summary>The frame as an SVG document exactly <paramref name="width"/> by <paramref name="height"/> points.</summary>
    internal static string Svg(float width, float height)
    {
        // The drawing keeps the shadow inside the box, so nothing is clipped at its edge.
        var w = Math.Max(width - ShadowOffset, 2 * (SideOrnament + Band + Corner) + 1);
        var h = Math.Max(height - ShadowOffset, Crest + Shell + 2 * (Band + Corner) + 1);
        var (x0, y0, x1, y1) = (SideOrnament, Crest, w - SideOrnament, h - Shell);
        var mid = w / 2;
        var middle = (y0 + y1) / 2;
        var curl = Math.Min((y1 - y0) * 0.32f, 26);

        // The C-scrolls: on each side one curling up and one curling down from the middle, and
        // along the top one either side of the crest.
        var scrolls = new List<string>();
        foreach (var dir in new[] { -1, 1 })
        {
            var x = dir < 0 ? x0 : x1;
            foreach (var v in new[] { -1, 1 })
            {
                scrolls.Add(SideScroll(x, middle, curl, dir, v));
            }

            scrolls.Add(TopScroll(mid + dir * (Crest - 2), y0, dir));
        }

        var svg = new StringBuilder();
        svg.Append(Invariant($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">"""));
        svg.Append($"""
            <defs>
            <linearGradient id="gold" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#FCEDB9"/><stop offset="30%" stop-color="#E4BC5C"/><stop offset="55%" stop-color="#B8862C"/><stop offset="78%" stop-color="#F0D58C"/><stop offset="100%" stop-color="#946619"/>
            </linearGradient>
            <linearGradient id="bevel" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#946619"/><stop offset="45%" stop-color="#D2A548"/><stop offset="100%" stop-color="#FBE7A6"/>
            </linearGradient>
            <radialGradient id="panel" cx="50%" cy="50%" r="70%">
            <stop offset="0%" stop-color="{Panel}"/><stop offset="65%" stop-color="{Panel}"/><stop offset="100%" stop-color="{PanelShade}"/>
            </radialGradient>
            </defs>
            """);

        // The shadow: the whole silhouette, offset down and right.
        svg.Append(Invariant($"""<g transform="translate({ShadowOffset} {ShadowOffset})" fill-opacity="0.35" stroke-opacity="0.35">"""));
        svg.Append($"""<g fill="#000000">{Path(Scooped(x0, y0, x1, y1, Corner))}{Path(Fan(mid, y0, Crest - 2, up: true))}{Path(Fan(mid, y1, Shell - 1, up: false))}</g>""");
        svg.Append(Invariant($"""<g fill="none" stroke="#000000" stroke-width="{ScrollWeight + 1.6f}" stroke-linecap="round">"""));
        scrolls.ForEach(d => svg.Append(Path(d)));
        svg.Append("</g></g>");

        // The scrolls as gilt wire: a dark edge, the gold, and a highlight along it.
        foreach (var (colour, weight) in new[] { (Edge, ScrollWeight + 1.6f), (GoldWire, ScrollWeight), (GoldLight, 0.9f) })
        {
            svg.Append(Invariant($"""<g fill="none" stroke="{colour}" stroke-width="{weight}" stroke-linecap="round">"""));
            scrolls.ForEach(d => svg.Append(Path(d)));
            svg.Append("</g>");
        }

        // The crest and the shell, then the frame, then the panel and its fillet.
        svg.Append($"""<g fill="url(#gold)" stroke="{Edge}" stroke-width="0.8">""");
        svg.Append(Path(Fan(mid, y0, Crest - 2, up: true)));
        svg.Append(Path(Fan(mid, y1, Shell - 1, up: false)));
        svg.Append(Path(Scooped(x0, y0, x1, y1, Corner)));
        svg.Append("</g>");
        svg.Append(Ribs(mid, y0, Crest - 2, up: true));
        svg.Append(Ribs(mid, y1, Shell - 1, up: false));

        svg.Append($"""<path d="{Scooped(x0 + Band * 0.5f, y0 + Band * 0.5f, x1 - Band * 0.5f, y1 - Band * 0.5f, Corner - 2)}" fill="url(#bevel)" stroke="{Edge}" stroke-width="0.5"/>""");
        svg.Append($"""<path d="{Scooped(x0 + Band, y0 + Band, x1 - Band, y1 - Band, Corner - 4)}" fill="url(#panel)" stroke="{Edge}" stroke-width="0.7"/>""");
        svg.Append($"""<path d="{Scooped(x0 + Band + 4, y0 + Band + 4, x1 - Band - 4, y1 - Band - 4, Corner - 6)}" fill="none" stroke="{GoldLine}" stroke-width="0.7"/>""");

        // A rosette nestled in each scooped corner, and a bead where each side's scrolls meet.
        // A rosette sits in the scoop, against its curve: its centre on the corner's diagonal.
        var inset = (Corner - RosetteRadius + 1.5f) / MathF.Sqrt(2);
        var rosettes = new[]
        {
            (x0 + inset, y0 + inset, RosetteRadius), (x1 - inset, y0 + inset, RosetteRadius),
            (x0 + inset, y1 - inset, RosetteRadius), (x1 - inset, y1 - inset, RosetteRadius),
            (x0 - 2.5f, middle, 5.2f), (x1 + 2.5f, middle, 5.2f),
        };
        foreach (var (cx, cy, r) in rosettes)
        {
            svg.Append(Invariant($"""<circle cx="{cx}" cy="{cy}" r="{r}" fill="url(#gold)" stroke="{Edge}" stroke-width="0.7"/>"""));
            svg.Append(Invariant($"""<circle cx="{cx}" cy="{cy}" r="{r * 0.42f}" fill="none" stroke="{Edge}" stroke-width="0.5"/>"""));
            svg.Append(Invariant($"""<circle cx="{cx - r * 0.25f}" cy="{cy - r * 0.28f}" r="{r * 0.2f}" fill="{GoldLight}"/>"""));
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Path(string d) => $"""<path d="{d}"/>""";

    /// <summary>A rectangle whose corners are scooped inward by quarter circles of radius <paramref name="r"/>.</summary>
    private static string Scooped(float x0, float y0, float x1, float y1, float r) => Invariant(
        $"M {x0 + r} {y0} L {x1 - r} {y0} A {r} {r} 0 0 0 {x1} {y0 + r} L {x1} {y1 - r} A {r} {r} 0 0 0 {x1 - r} {y1} L {x0 + r} {y1} A {r} {r} 0 0 0 {x0} {y1 - r} L {x0} {y0 + r} A {r} {r} 0 0 0 {x0 + r} {y0} Z");

    /// <summary>
    /// A C-scroll leaving the frame's side at its middle and curling, away from the middle, into a
    /// spiral that turns back toward the frame. <paramref name="dir"/> is -1 on the left, 1 on the
    /// right; <paramref name="v"/> is -1 for the upper scroll, 1 for the lower.
    /// </summary>
    private static string SideScroll(float x, float middle, float curl, int dir, int v)
    {
        var o = SideOrnament * dir;
        var (ax, ay) = (x + o * 0.72f, middle + v * curl * 0.78f);
        return Invariant($"M {x} {middle + v * 3} C {x + o * 0.25f} {middle + v * 3} {x + o * 0.9f} {middle + v * curl * 0.25f} {ax} {ay} ") +
            Spiral(ax, ay, -dir, sweep: dir * v > 0, 6f);
    }

    /// <summary>A small scroll lying along the frame's top from beside the crest, curling outward and down.</summary>
    private static string TopScroll(float x, float y, int dir)
    {
        var (ax, ay) = (x + dir * 30, y - 6);
        return Invariant($"M {x} {y - 1.5f} C {x + dir * 10} {y - 1.5f} {x + dir * 20} {y - 8} {ax} {ay} ") +
            Spiral(ax, ay, dir, sweep: dir > 0, 3.6f);
    }

    /// <summary>
    /// A spiral of three shrinking half turns from (<paramref name="x"/>, <paramref name="y"/>),
    /// its first half turn ending <paramref name="first"/> × 2 toward <paramref name="toward"/>, as
    /// arc segments continuing the path.
    /// </summary>
    private static string Spiral(float x, float y, int toward, bool sweep, float first)
    {
        var flag = sweep ? 1 : 0;
        var r1 = first;
        var r2 = first * 0.62f;
        var r3 = first * 0.38f;
        var x1 = x + toward * 2 * r1;
        var x2 = x1 - toward * 2 * r2;
        var x3 = x2 + toward * 2 * r3;
        return Invariant($"A {r1} {r1} 0 0 {flag} {x1} {y} A {r2} {r2} 0 0 {flag} {x2} {y} A {r3} {r3} 0 0 {flag} {x3} {y}");
    }

    /// <summary>A half-disc fan standing on the frame's edge at (<paramref name="x"/>, <paramref name="y"/>), above it or below it.</summary>
    private static string Fan(float x, float y, float r, bool up) => Invariant(
        $"M {x - r} {y} A {r} {r} 0 0 {(up ? 1 : 0)} {x + r} {y} Z");

    /// <summary>The fan's ribs, fanning from its centre.</summary>
    private static string Ribs(float x, float y, float r, bool up)
    {
        var sb = new StringBuilder($"""<g stroke="{Edge}" stroke-width="0.6">""");
        for (var i = 1; i < 6; i++)
        {
            var angle = Math.PI * i / 6;
            var dx = (float)(Math.Cos(angle) * r * 0.92);
            var dy = (float)(Math.Sin(angle) * r * 0.92) * (up ? -1 : 1);
            sb.Append(Invariant($"""<line x1="{x}" y1="{y}" x2="{x + dx}" y2="{y + dy}"/>"""));
        }

        return sb.Append("</g>").ToString();
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
