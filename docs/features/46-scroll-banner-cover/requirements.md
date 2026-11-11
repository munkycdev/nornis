# Requirements Document

## Introduction

On 2026-11-11 Dave asked for a fourth printed cover layout: the cover art fills the whole cover,
and a scroll-type image sits under the title to separate the text from the art. On the first
proposal (the author line on a second, smaller scroll at the foot) he asked to loosen feature 38's
"type never sits on the art" rule instead: set the author line straight on the art, in a colour
picked for contrast with what's behind it.

Ships as Nornis 3.29. No migration: `CoverLayout` is stored as a string of up to 20 characters,
and `ScrollBanner` is 12.

## Requirements

### Requirement 1: The scroll banner layout

**User Story:** As a GM, I want a cover where the art fills the page and the title sits on a
parchment scroll, so the cover looks like an adventure's and the title still reads.

1. The Cover card's Layout list SHALL offer **Scroll banner** beside Full bleed, Framed and
   Classic module.
2. With Scroll banner, the art SHALL fill the whole page.
3. The title, game system and levels SHALL sit on a parchment scroll near the top of the page.
   The scroll SHALL be drawn to the size the type needs, so a three-line title gets a taller
   scroll, never a stretched one, and SHALL be opaque, so the title never depends on the art.
4. The title SHALL be clamped to three lines, as on the other art layouts; the cover SHALL stay
   one page whatever the title.
5. The Loremaster SHALL see the layout described as "scroll banner".

### Requirement 2: The author line on the art

**User Story:** As a GM, I want my name on the art without another box around it, and I want it
readable whatever the art looks like.

1. The author line SHALL be set on the art near the foot of the page, centred.
2. Its colour SHALL be white or the book's ink, whichever has the higher WCAG contrast against the
   mean luminance of the strip of the printed (cropped) art behind it.
3. When more than 10% of that strip falls under 3:1 contrast against the chosen colour, the line
   SHALL get a soft translucent shade behind it (dark under white type, light under ink) that
   fades out at its edge.
4. Art that can't be read SHALL never fail the print: it falls back to the typeset cover as
   today, and the contrast check alone falls back to white on a shade.
