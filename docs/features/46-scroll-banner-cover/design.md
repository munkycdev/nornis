# Design Document

## Overview

A fourth `CoverLayout`, `ScrollBanner`, rendered by `QuestPdfBookletRenderer.ScrollBannerCover`.
Everything new lives in `Nornis.Infrastructure/Printing`; the other layers only learn a new enum
value.

## Decisions

- **A layout, not a modifier.** A "title treatment" switch would only mean anything over full
  bleed art and would need a new column. A new enum value needs neither.
- **The frame is drawn, not bundled or generated** (`GildedFrameArt.Svg(width, height)`).
  QuestPDF's dynamic SVG hands the drawing the size of the type it sits behind (a `Layers`
  background), so the frame is drawn to fit: an ivory panel in a bevelled gold frame with
  scooped corners, a rosette in each corner, gilt C-scrolls ending in spirals on each side and
  along the top, a fan crest above and a small shell below. The scrolls are stroked three times
  (dark edge, gold, highlight) to read as gilt wire. Vector, so it prints sharp; no licensed
  asset; no extra AI spend; deterministic. The first build was a parchment scroll with rolled
  ends; Dave asked for something more ornate after seeing it. A bundled PNG texture and a generated scroll were considered
  and declined (a stretched fixed shape and about 1 to 2 MB per booklet; cost, no repeatability
  and no reliable clean space for type).
- **The author line's colour is measured, not chosen** (`ArtContrast`). It decodes the very
  cropped JPEG the page prints (`CoverArtImage.Fit`, then `CoverArtImage.Decode`) and samples the
  strip `QuestPdfBookletRenderer.AuthorStrip` names, at most 20,000 pixels. White or ink by WCAG
  contrast against the mean luminance; a shade when over 10% of the strip is under 3:1 against
  that colour. The shade is nested translucent ellipses: Skia's SVG drops gradient stop
  opacity, so a radial gradient to transparent draws as a solid oval.
- **Numbers in SVG are written invariantly.** A culture that writes "1,5" would otherwise break
  the drawing silently; `ArtContrastTests` pins it under de-DE.

## Testing

- `QuestPdfBookletRendererTests`: Scroll banner joins the one-page and longest-title cases; the
  art fills the page, the title and system line are in the top fifth, the author at the foot;
  the author line is white over dark art and ink over light art.
- `ArtContrastTests`: dark, light, uneven and unreadable art; only the strip is judged; WCAG
  ratios; well-formed SVG under de-DE.
- `CoverCardTests` / `CoverCardMirrorTests`: the card offers the four enum values.
