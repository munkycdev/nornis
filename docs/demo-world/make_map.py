"""Generator for the demo-world campaign map: 'The Vespergale Reach'.

Renders a parchment survey map as PNG. Hand-drawn quality comes from
supersampling, jittered strokes, and accumulated small glyphs rather than
filters. Labels are kept crisp and high-contrast on purpose: the Nornis map
extraction pipeline must be able to read them.

Run:  python make_map.py   ->  map.png (2000x1400)
"""

from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 2                       # supersample factor
W, H = 2000 * S, 1400 * S   # working canvas
OUT = Path(__file__).parent / "map.png"

FONTS = Path(r"C:\Users\david\AppData\Roaming\Claude\local-agent-mode-sessions"
             r"\skills-plugin\85e70986-443c-47f9-aa20-43983859de04"
             r"\094d1888-0716-4867-8535-83d6c08e30de\skills\canvas-design\canvas-fonts")

PARCH = (233, 220, 188)
PARCH_DARK = (208, 190, 150)
INK = (74, 56, 38)
INK_FAINT = (74, 56, 38, 90)
SEA_WASH = (146, 152, 138, 30)

MARGIN = 60 * S             # outer border rule
INNER = 96 * S              # terrain inset

random.seed(31)
rng = np.random.default_rng(31)


def px(nx: float, ny: float) -> tuple[float, float]:
    """Normalized 0..1 map coords -> pixels inside the inner frame."""
    return (INNER + nx * (W - 2 * INNER), INNER + ny * (H - 2 * INNER))


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / name), size * S)


# ---------------------------------------------------------------- stroke helpers

def jitter_line(draw, pts, width, color, amp=1.6):
    """Polyline with per-vertex tremor, drawn as overlapping segments."""
    out = [(x + random.uniform(-amp, amp) * S, y + random.uniform(-amp, amp) * S)
           for x, y in pts]
    draw.line(out, fill=color, width=max(1, int(width)), joint="curve")
    return out


def chaikin(pts, rounds=3):
    for _ in range(rounds):
        nxt = [pts[0]]
        for a, b in zip(pts, pts[1:]):
            nxt.append((0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]))
            nxt.append((0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1]))
        nxt.append(pts[-1])
        pts = nxt
    return pts


def displace(pts, iterations=4, amp=0.018):
    """Fractal midpoint displacement in normalized space."""
    for _ in range(iterations):
        nxt = []
        for a, b in zip(pts, pts[1:]):
            nxt.append(a)
            mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
            nxt.append((mx + random.uniform(-amp, amp), my + random.uniform(-amp, amp)))
        nxt.append(pts[-1])
        pts, amp = nxt, amp * 0.5
    return pts


def dashed(draw, pts, width, color, dash=14, gap=10):
    dash, gap = dash * S, gap * S
    dist_on = 0.0
    pen_down = True
    last = pts[0]
    for cur in pts[1:]:
        seg = math.dist(last, cur)
        if seg == 0:
            continue
        t = 0.0
        while t < seg:
            step = min((dash if pen_down else gap) - dist_on, seg - t)
            a = (last[0] + (cur[0] - last[0]) * t / seg,
                 last[1] + (cur[1] - last[1]) * t / seg)
            t2 = t + step
            b = (last[0] + (cur[0] - last[0]) * t2 / seg,
                 last[1] + (cur[1] - last[1]) * t2 / seg)
            if pen_down:
                draw.line([a, b], fill=color, width=width)
            dist_on += step
            if dist_on >= (dash if pen_down else gap):
                pen_down, dist_on = not pen_down, 0.0
            t = t2
        last = cur


# ---------------------------------------------------------------- parchment

def parchment() -> Image.Image:
    base = np.ones((H, W, 3), dtype=np.float32)
    base[..., 0] *= PARCH[0]
    base[..., 1] *= PARCH[1]
    base[..., 2] *= PARCH[2]

    for scale, strength in ((6, 10), (24, 8), (96, 7)):
        small = rng.normal(0, 1, (H // (scale * S), W // (scale * S)))
        noise = np.array(Image.fromarray(
            ((small - small.min()) / (np.ptp(small) + 1e-6) * 255).astype(np.uint8)
        ).resize((W, H), Image.BILINEAR), dtype=np.float32)
        base += (noise[..., None] - 127.5) / 127.5 * strength

    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    dx, dy = (xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)
    d = np.sqrt(dx * dx + dy * dy)
    base *= (1.0 - 0.16 * np.clip(d - 0.35, 0, 1.2) ** 1.6)[..., None]

    img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8))

    stains = Image.new("L", (W, H), 0)
    sd = ImageDraw.Draw(stains)
    for _ in range(26):
        cx, cy = random.uniform(0, W), random.uniform(0, H)
        r = random.uniform(60, 260) * S
        sd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=random.randint(8, 22))
    stains = stains.filter(ImageFilter.GaussianBlur(60 * S))
    img = Image.composite(Image.new("RGB", (W, H), PARCH_DARK), img, stains)
    return img


# ---------------------------------------------------------------- terrain

COAST = [(0.0, 0.50), (0.05, 0.54), (0.10, 0.52), (0.145, 0.575), (0.135, 0.64),
         (0.175, 0.695), (0.215, 0.655), (0.255, 0.625), (0.30, 0.645),
         (0.345, 0.685), (0.40, 0.72), (0.47, 0.735), (0.53, 0.755),
         (0.60, 0.745), (0.66, 0.775), (0.72, 0.77), (0.79, 0.80),
         (0.87, 0.795), (0.94, 0.825), (1.0, 0.835)]

RIVER = [(0.475, 0.155), (0.465, 0.21), (0.435, 0.265), (0.452, 0.315),
         (0.435, 0.365), (0.40, 0.415), (0.378, 0.465), (0.355, 0.515),
         (0.33, 0.565), (0.305, 0.61), (0.295, 0.658)]

FOREST_C, FOREST_R = (0.69, 0.40), (0.115, 0.095)


def draw_sea(ink, terrain_pts):
    d = ImageDraw.Draw(ink)
    poly = [px(*p) for p in terrain_pts] + [(W - INNER, H - INNER), (INNER, H - INNER)]
    d.polygon(poly, fill=SEA_WASH)
    # Offshore contour lines: the coast redrawn, pushed seaward, fading out.
    for i, alpha in ((1, 70), (2, 46), (3, 26)):
        off = [(x, y + 13 * S * i) for x, y in (px(*p) for p in terrain_pts)]
        jitter_line(d, chaikin(off, 2), 1.6 * S, (*INK, alpha), amp=1.2)


def draw_coast(ink):
    pts = displace(COAST, 4, 0.012)
    pts_px = chaikin([px(*p) for p in pts], 2)
    d = ImageDraw.Draw(ink)
    jitter_line(d, pts_px, 3.0 * S, (*INK, 235), amp=1.0)
    jitter_line(d, [(x, y + 4 * S) for x, y in pts_px], 1.4 * S, (*INK, 110), amp=1.4)
    return pts_px


def draw_river(ink):
    pts = chaikin([px(*p) for p in displace(RIVER, 3, 0.008)], 3)
    d = ImageDraw.Draw(ink)
    n = len(pts)
    for i in range(n - 1):
        w = 1.2 + 2.6 * (i / n)      # taper: thin at source, wide at mouth
        d.line([pts[i], pts[i + 1]], fill=(*INK, 220), width=int(w * S))
    return pts


def peak(d, x, y, s):
    skew = random.uniform(-0.15, 0.15)
    top = (x + skew * s, y - s)
    d.line([(x - s, y), top], fill=(*INK, 210), width=int(1.8 * S))
    d.line([top, (x + s * 0.95, y + s * 0.05)], fill=(*INK, 210), width=int(1.8 * S))
    for k in range(2, 5):           # shading strokes on the right face
        t = k / 5
        a = (top[0] + (x + s * 0.95 - top[0]) * t * 0.55,
             top[1] + (y - top[1]) * t * 0.55)
        b = (a[0] + s * 0.34 * t, a[1] + s * 0.5 * t)
        d.line([a, b], fill=(*INK, 120), width=int(1.1 * S))


def draw_mountains(ink, keep_clear):
    d = ImageDraw.Draw(ink)
    spots = []
    # Main ridge: two staggered rows following a gentle arc across the north.
    for row, (y0, count, size) in enumerate(((0.115, 26, 30), (0.165, 20, 22))):
        for i in range(count):
            nx = 0.045 + (0.91 / (count - 1)) * i + random.uniform(-0.012, 0.012)
            ny = y0 + 0.035 * math.sin(nx * 6.5) + random.uniform(-0.014, 0.014)
            spots.append((nx, ny, size * random.uniform(0.7, 1.15)))
    # Eastern foothills reaching toward the abbey.
    for i in range(9):
        nx = 0.60 + 0.30 * (i / 8) + random.uniform(-0.015, 0.015)
        ny = 0.225 + random.uniform(-0.012, 0.02)
        spots.append((nx, ny, 13 * random.uniform(0.7, 1.1)))
    for nx, ny, size in spots:
        x, y = px(nx, ny)
        if any(math.dist((x, y), px(*c)) < r * S for c, r in keep_clear):
            continue
        peak(d, x, y, size * S)


def tree(d, x, y, s):
    d.ellipse([x - s, y - 1.9 * s, x + s, y - 0.1 * s], outline=(*INK, 180),
              width=int(1.2 * S))
    d.line([(x, y - 0.1 * s), (x, y + 0.55 * s)], fill=(*INK, 180),
           width=int(1.2 * S))


def draw_moors(ink, keep_clear):
    """Sparse grass tufts so the western plains read as surveyed, not skipped."""
    d = ImageDraw.Draw(ink)
    placed = 0
    tries = 0
    while placed < 30 and tries < 3000:
        tries += 1
        nx, ny = random.uniform(0.06, 0.34), random.uniform(0.24, 0.52)
        x, y = px(nx, ny)
        if any(math.dist((x, y), px(*c)) < (r + 26) * S for c, r in keep_clear):
            continue
        s = random.uniform(5, 8) * S
        for ang in (-0.5, -0.12, 0.28):
            d.line([(x, y), (x + s * math.sin(ang) * 1.4, y - s * math.cos(ang))],
                   fill=(*INK, 95), width=int(1.1 * S))
        placed += 1


def draw_forest(ink, keep_clear):
    d = ImageDraw.Draw(ink)
    placed = []
    tries = 0
    while len(placed) < 46 and tries < 4000:
        tries += 1
        ang, rad = random.uniform(0, 2 * math.pi), math.sqrt(random.random())
        nx = FOREST_C[0] + math.cos(ang) * FOREST_R[0] * rad
        ny = FOREST_C[1] + math.sin(ang) * FOREST_R[1] * rad
        x, y = px(nx, ny)
        if any(math.dist((x, y), p) < 26 * S for p in placed):
            continue
        if any(math.dist((x, y), px(*c)) < r * S for c, r in keep_clear):
            continue
        placed.append((x, y))
    for x, y in placed:
        tree(d, x, y, random.uniform(7, 11) * S)


# ---------------------------------------------------------------- sites

SITES = [
    # name, nx, ny, glyph, label side
    ("HARROWPORT",       0.295, 0.615, "city",  "left"),
    ("LANTERNWATCH",     0.135, 0.655, "tower", "below"),
    ("WINDROW",          0.398, 0.475, "town",  "below"),
    ("THE SUNKEN FERRY", 0.448, 0.352, "ruin",  "right"),
    ("THORNHOLLOW",      0.565, 0.455, "town",  "below"),
    ("GREYHOLLOW ABBEY", 0.745, 0.275, "ruin",  "right"),
    ("BLEAKSPIRE KEEP",  0.49,  0.128, "keep",  "right"),
    ("THE CINDER TOR",   0.555, 0.60,  "tor",   "below"),
    ("SALTMERE",         0.78,  0.715, "town",  "left"),
]

ROADS = [
    [(0.295, 0.615), (0.34, 0.55), (0.398, 0.475)],
    [(0.398, 0.475), (0.425, 0.41), (0.448, 0.352)],
    [(0.448, 0.352), (0.55, 0.30), (0.65, 0.275), (0.745, 0.275)],
    [(0.398, 0.475), (0.47, 0.54), (0.555, 0.60)],
    [(0.555, 0.60), (0.66, 0.66), (0.78, 0.715)],
    [(0.565, 0.455), (0.49, 0.465), (0.398, 0.475)],
]


def glyph(d, kind, x, y):
    s = 7 * S
    if kind == "town":
        d.ellipse([x - s, y - s, x + s, y + s], outline=(*INK, 255), width=int(1.8 * S))
        d.ellipse([x - 2 * S, y - 2 * S, x + 2 * S, y + 2 * S], fill=(*INK, 255))
    elif kind == "city":
        d.ellipse([x - s, y - s, x + s, y + s], outline=(*INK, 255), width=int(1.8 * S))
        d.ellipse([x - s * 1.7, y - s * 1.7, x + s * 1.7, y + s * 1.7],
                  outline=(*INK, 200), width=int(1.4 * S))
        d.ellipse([x - 2 * S, y - 2 * S, x + 2 * S, y + 2 * S], fill=(*INK, 255))
    elif kind in ("tower", "keep"):
        w, h = (5 * S, 13 * S) if kind == "tower" else (7 * S, 12 * S)
        d.rectangle([x - w, y - h, x + w, y + 2 * S], outline=(*INK, 255),
                    width=int(1.8 * S))
        for i in (-1, 0, 1):        # battlements
            d.rectangle([x + i * w * 0.7 - 1.5 * S, y - h - 3 * S,
                         x + i * w * 0.7 + 1.5 * S, y - h], fill=(*INK, 255))
    elif kind == "ruin":
        for dx2, hh in ((-6, 9), (0, 13), (6, 6)):
            d.rectangle([x + dx2 * S - 2 * S, y - hh * S, x + dx2 * S + 2 * S, y],
                        outline=(*INK, 230), width=int(1.5 * S))
    elif kind == "tor":
        d.arc([x - 16 * S, y - 13 * S, x + 16 * S, y + 13 * S], 180, 360,
              fill=(*INK, 230), width=int(2.0 * S))
        d.arc([x - 9 * S, y - 7 * S, x + 9 * S, y + 8 * S], 180, 360,
              fill=(*INK, 150), width=int(1.3 * S))
        d.line([(x, y - 13 * S), (x, y - 21 * S)], fill=(*INK, 230), width=int(1.8 * S))
        d.line([(x - 4 * S, y - 17 * S), (x + 4 * S, y - 17 * S)],
               fill=(*INK, 230), width=int(1.6 * S))


def tracked_text(d, xy, text, fnt, fill, tracking, anchor="la", halo=None):
    """Letterspaced text with an optional parchment halo, per character."""
    widths = [d.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + tracking * S * (len(text) - 1)
    x, y = xy
    if anchor[0] == "m":
        x -= total / 2
    elif anchor[0] == "r":
        x -= total
    for ch, w in zip(text, widths):
        if halo:
            for ox in (-2, 0, 2):
                for oy in (-2, 0, 2):
                    d.text((x + ox * S, y + oy * S), ch, font=fnt, fill=halo)
        d.text((x, y), ch, font=fnt, fill=fill)
        x += w + tracking * S
    return total


def draw_sites(ink):
    d = ImageDraw.Draw(ink)
    label_f = font("CrimsonPro-Bold.ttf", 27)
    halo = (*PARCH, 210)
    for name, nx, ny, kind, side in SITES:
        x, y = px(nx, ny)
        glyph(d, kind, x, y)
        if side == "right":
            tracked_text(d, (x + 16 * S, y - 17 * S), name, label_f, (*INK, 255), 2.4,
                         "la", halo)
        elif side == "left":
            tracked_text(d, (x - 16 * S, y - 17 * S), name, label_f, (*INK, 255), 2.4,
                         "ra", halo)
        else:  # below
            tracked_text(d, (x, y + 16 * S), name, label_f, (*INK, 255), 2.4,
                         "ma", halo)


def draw_roads(ink):
    d = ImageDraw.Draw(ink)
    for road in ROADS:
        pts = chaikin([px(*p) for p in displace(road, 3, 0.006)], 2)
        dashed(d, pts, int(1.6 * S), (*INK, 150), dash=10, gap=8)


# ---------------------------------------------------------------- lettering & frame

def rotated_text(base, center, text, fnt, fill, angle, tracking=2.0):
    pad = 40 * S
    tmp = Image.new("RGBA", (int(len(text) * fnt.size * 1.2) + pad * 2,
                             fnt.size * 2 + pad * 2), (0, 0, 0, 0))
    td = ImageDraw.Draw(tmp)
    w = tracked_text(td, (pad, pad), text, fnt, fill, tracking)
    tmp = tmp.crop((pad - 10 * S, pad - 10 * S, pad + w + 10 * S,
                    pad + fnt.size * 1.5 + 10 * S))
    tmp = tmp.rotate(angle, expand=True, resample=Image.BICUBIC)
    base.alpha_composite(tmp, (int(center[0] - tmp.width / 2),
                               int(center[1] - tmp.height / 2)))


def draw_features(ink):
    d = ImageDraw.Draw(ink)
    it34 = font("CrimsonPro-Italic.ttf", 34)
    it46 = font("CrimsonPro-Italic.ttf", 46)
    tracked_text(d, px(0.42, 0.875), "T h e   V e s p e r g a l e", it46,
                 (*INK, 170), 4.0, "ma")
    tracked_text(d, px(0.255, 0.055), "T H E   B L E A K S P I N E", it34,
                 (*INK, 160), 3.0, "ma")
    tracked_text(d, px(0.69, 0.475), "The Mistwood", it34, (*INK, 170), 2.0, "ma")
    rotated_text(ink, px(0.408, 0.30), "Silverwithe", font("CrimsonPro-Italic.ttf", 28),
                 (*INK, 185), 68)


def compass(ink, cx, cy, r):
    d = ImageDraw.Draw(ink)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(*INK, 210), width=int(1.6 * S))
    d.ellipse([cx - r * 0.72, cy - r * 0.72, cx + r * 0.72, cy + r * 0.72],
              outline=(*INK, 120), width=int(1.0 * S))
    for i in range(8):
        a = math.pi / 4 * i
        long = i % 2 == 0
        rr = r * (0.95 if long else 0.55)
        tip = (cx + rr * math.sin(a), cy - rr * math.cos(a))
        base_r = r * 0.14
        left = (cx + base_r * math.sin(a - math.pi / 2), cy - base_r * math.cos(a - math.pi / 2))
        right = (cx + base_r * math.sin(a + math.pi / 2), cy - base_r * math.cos(a + math.pi / 2))
        if long:
            d.polygon([tip, left, (cx, cy)], outline=(*INK, 220), width=int(1.2 * S))
            d.polygon([tip, right, (cx, cy)], fill=(*INK, 190))
        else:
            d.line([(cx, cy), tip], fill=(*INK, 160), width=int(1.2 * S))
    d.text((cx, cy - r - 26 * S), "N", font=font("CrimsonPro-Bold.ttf", 30),
           fill=(*INK, 235), anchor="mm")


def scale_bar(ink, x, y):
    d = ImageDraw.Draw(ink)
    seg, hgt = 52 * S, 7 * S
    for i in range(4):
        fill = (*INK, 210) if i % 2 == 0 else None
        d.rectangle([x + i * seg, y, x + (i + 1) * seg, y + hgt],
                    outline=(*INK, 210), width=int(1.2 * S), fill=fill)
    f = font("CrimsonPro-Regular.ttf", 21)
    for i, lab in ((0, "0"), (2, "10"), (4, "20")):
        d.text((x + i * seg, y + hgt + 6 * S), lab, font=f, fill=(*INK, 200), anchor="ma")
    d.text((x + 4 * seg + 16 * S, y - 2 * S), "leagues", font=font("CrimsonPro-Italic.ttf", 22),
           fill=(*INK, 200), anchor="la")


def cartouche(ink):
    d = ImageDraw.Draw(ink)
    x0, y0 = px(0.645, 0.855)
    x1, y1 = px(0.975, 0.975)
    d.rectangle([x0, y0, x1, y1], outline=(*INK, 230), width=int(2.2 * S),
                fill=(*PARCH, 235))
    g = 6 * S
    d.rectangle([x0 + g, y0 + g, x1 - g, y1 - g], outline=(*INK, 140), width=int(1.0 * S))
    cx = (x0 + x1) / 2
    title_f = font("Italiana-Regular.ttf", 34)
    tracked_text(d, (cx, y0 + 22 * S), "THE VESPERGALE REACH",
                 title_f, (*INK, 255), 2.2, "ma")
    tracked_text(d, (cx, y0 + 74 * S), "being a survey of the coast & hinterland",
                 font("CrimsonPro-Italic.ttf", 23), (*INK, 190), 0.8, "ma")


def frame(ink):
    d = ImageDraw.Draw(ink)
    d.rectangle([MARGIN, MARGIN, W - MARGIN, H - MARGIN],
                outline=(*INK, 235), width=int(2.4 * S))
    m2 = MARGIN + 9 * S
    d.rectangle([m2, m2, W - m2, H - m2], outline=(*INK, 130), width=int(1.1 * S))
    step = 100 * S
    for x in range(int(MARGIN + step), int(W - MARGIN), int(step)):
        d.line([(x, MARGIN), (x, MARGIN + 5 * S)], fill=(*INK, 140), width=int(1.1 * S))
        d.line([(x, H - MARGIN - 5 * S), (x, H - MARGIN)], fill=(*INK, 140), width=int(1.1 * S))
    for y in range(int(MARGIN + step), int(H - MARGIN), int(step)):
        d.line([(MARGIN, y), (MARGIN + 5 * S, y)], fill=(*INK, 140), width=int(1.1 * S))
        d.line([(W - MARGIN - 5 * S, y), (W - MARGIN, y)], fill=(*INK, 140), width=int(1.1 * S))


# ---------------------------------------------------------------- main

def main():
    ground = parchment().convert("RGBA")
    ink = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    coast_disp = displace(COAST, 4, 0.012)
    draw_sea(ink, coast_disp)
    draw_coast(ink)
    draw_river(ink)

    clear = ([((nx, ny), 34) for _, nx, ny, _, _ in SITES]
             + [(p, 22) for p in RIVER])
    draw_mountains(ink, clear)
    draw_forest(ink, clear)
    draw_moors(ink, clear)
    draw_roads(ink)
    draw_sites(ink)
    draw_features(ink)

    compass(ink, *px(0.115, 0.845), 52 * S)
    scale_bar(ink, *px(0.055, 0.955))
    cartouche(ink)
    frame(ink)

    out = Image.alpha_composite(ground, ink).convert("RGB")

    grain = rng.normal(0, 3.2, (H, W, 1)).astype(np.float32)
    arr = np.clip(np.asarray(out, dtype=np.float32) + grain, 0, 255).astype(np.uint8)
    out = Image.fromarray(arr).resize((W // S, H // S), Image.LANCZOS)
    out.save(OUT)
    print(f"wrote {OUT} ({out.width}x{out.height})")


if __name__ == "__main__":
    main()
