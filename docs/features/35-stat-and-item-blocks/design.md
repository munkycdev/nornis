# Design Document

> **Amendment (2026-11-03, Phases B and C):** what building it changed. The text below is
> left as written.
>
> - **A GM callout.** Dave's Slurping Spa transcript showed a box the grammar lacked: a note for
>   the GM alone (a hard exit, a fallback). `> [!GM]` (`PrepMarkup.GmMarker`) is a marked quote
>   like Prop and Ask, with a "GM note" editor button (`Icons.Material.Outlined.Flag`) and a
>   shaded box with a heavy ink left rule in the booklet (`BookletBoxKind.Gm`). The prompt
>   conventions teach it.
> - **Prompts write for the adventure's own system.** Drafting and the side conversation used
>   the world's game system and ignored Book details. Both now read `PrintDetails.SystemOf`
>   (the adventure's system or the world's, then its edition), the same resolution the booklet
>   cover uses, and name the adventure's levels when it has them.
> - **The cut never depends on the system.** The warnings are built where no system is at
>   hand (a save, a draft), so an export line keeps `SystemReserveChars` (the longest system
>   plus edition, with " ()") free and decides its cut on the block alone. The warning and the
>   export can't disagree. A name is cut at 200 characters, so the head alone can't fill the
>   line. `GameBlockWarnings.For` therefore takes the Markdown only.
> - **The export line's shape** is `Stat block for "Name" (System): *subtitle*. A: 1; B: 2.
>   Group — C: 3.` An entry's own full stop gives way to the separator. An unclosed fence still
>   becomes its line.
> - **The export falls back to fences.** A block line can be longer than its fence (the head
>   and the system), so only the body without lines is inside `AdventureSize`'s bound. When the
>   body with lines would pass `SourceService.MaxBodyChars`, the export service composes it
>   again without them (`blockLines: false`) and logs it; the export still goes through whole.
> - **Groups print as run-in entries** ("Cutlass. +5 to hit…", label in semibold italics), not
>   a label table, which reads like a published stat block. A group's heading, its rule and its
>   first entry are kept together, so a heading never ends a column.
> - **The item frame** is a 2 pt ink line above a hairline left, right and bottom border:
>   QuestPDF gives a container's sides one colour, so the top rule is its own element.
> - **The worked example** in the prompt conventions uses plain labels (Defense, Health, Speed)
>   rather than one system's, so it doesn't pull every draft towards that system.
> - **Warnings are plain text**, shown one per line in a dense warning alert.

## Overview

Game blocks stay Markdown fences in a section's text. One parser in Application reads them into
a typed shape, and every reader uses it:

```
section BodyMarkdown ──► GameBlocks.Parse(info, lines) ──► GameBlock
                                                │
        ┌───────────────────────┬───────────────┼──────────────────────┐
  BookletModel             GameBlockWarnings     AdventureExportComposer
  (booklet records)        (section responses)   (one line per named block)
        │                                                │
  QuestPdfBookletRenderer                         extraction prompt: one fact
```

There is no migration. A block written before this feature parses to the same entries it
printed as before.

## Grammar

```statblock
# Captain Voss
*Medium human, pirate captain*
AC: 15
HP: 27 (5d8+5)
Speed: 30 ft., swim 20 ft.
STR: 14
DEX: 16
CON: 12
## Traits
Sea legs: Advantage on saves against being knocked prone on a deck.
## Actions
Cutlass: +5 to hit, 1d8+3 slashing.
```

```item
# Cloak of the Tidecaller
*Wondrous item, rare, requires attunement*
Weight: 1 lb
Value: 400 gp
---
While you wear this cloak you can breathe water, and once per dawn you can call a wave.
```

Each line, trimmed, is read in this order:

| Line | Meaning | Where |
| ---- | ------- | ----- |
| `# text`, as the first non-empty line | name | both |
| `*text*`, straight after the name | subtitle | both |
| `## text` | starts a group | stat block |
| `---` | ends the entries; the rest is description | item |
| `label: value`, with at least one character before the colon | entry | both |
| anything else | text line | both |

- **Legacy.** A first entry labelled `Name` (any case) is read as the name. That is how the
  feature 29 template wrote it, so old blocks gain a name.
- **In an item block,** `## text` is a text line, and so is `---` in a stat block. Each block
  has only the structure its kind needs.
- **Description lines** keep their own line breaks and are never parsed as entries, so a colon
  in prose is safe.

### The shape

```csharp
// Application/Prep/GameBlocks.cs
public enum GameBlockKind { Stat, Item }
public sealed record GameBlock(
    GameBlockKind Kind,
    string? Name,
    string? Subtitle,
    IReadOnlyList<GameBlockGroup> Groups,      // the first has Heading null: the lines before any ##
    IReadOnlyList<string> Description);         // item only; empty for a stat block
public sealed record GameBlockGroup(string? Heading, IReadOnlyList<GameBlockLine> Lines);
public sealed record GameBlockLine(string? Label, string Value);  // Label null = a text line

public static class GameBlocks
{
    public static bool IsGameBlock(string? info);              // statblock | item
    public static GameBlock Parse(string info, IEnumerable<string> lines);
    public static IEnumerable<GameBlock> InMarkdown(string? markdown); // every game block in a body, in order
}
```

`PrepMarkup` gains `ItemBlockInfo = "item"`. The Web's mirror gains it too, and the existing
`PrepMarkupMirrorTests` pins it.

## Writing

### Prompts (one place)

The drafting and conversation prompts each carry their own copy of the Markdown conventions
today. That is the same rule in two places (pre-implementation check 1). It moves to
`PrepMarkup.ConventionsForPrompt`, a single block of text including the worked example above.
Both prompts interpolate it, and a test asserts both contain it verbatim.

The new rules sit in that block:

- Use the world's game system's usual labels if it has one, and plain labels otherwise. Never
  compute a number.
- When the world record already holds a creature's `stat block` fact or an item's
  `item details` fact, reuse it.
- Don't reproduce a published book's stat block. Name the book and the page ("see *Monsters*,
  p. 123") and give only what the scene needs.

### Editor

- The Stat block button inserts the new-grammar template: name, subtitle, three entries and an
  `## Actions` group with one entry.
- A new Item button (`Icons.Material.Outlined.Backpack`) inserts the item template. It shows
  only in Prep mode, beside Stat block.
- The templates stay in the Web's `PrepMarkup`, as the Stat block template does today. They
  are what the toolbar inserts, not a rule.

## Block warnings

`GameBlockWarnings.For(string? markdown, string? gameSystem)` is pure and lives in
Application/Prep. It returns `IReadOnlyList<string>`:

- "*Captain Voss*'s stat block is too long to file as one fact; Export to Nornis will cut it
  after *Actions*."
- "The second stat block has no name, so Export to Nornis can't file it on anyone. Start it
  with `# Name`."
- "The first item block is empty."

`AdventureSectionResponse` gains `IReadOnlyList<string> BlockWarnings`, computed when the
response is built. A response is built on every path: load, save, draft, Put in section and
import. The Web shows the list as a dense warning alert under the section's editor.

The warnings read the export line's length through the composer's own function, so the
warning and the cut can't disagree (check 1).

## Booklet

`BookletStatBlock` is replaced by `BookletGameBlock(GameBlock Block)`. The model only parses;
the renderer decides the look.

Stat block:

- The name is in Sans SemiBold 11 pt, in the accent colour, over a 1.5 pt accent rule. The
  subtitle is Serif italic 9 pt.
- Then come the ungrouped lines, then each group under its heading in Sans 8 pt small caps
  with a hairline rule.
- **Score row.** A run of three or more consecutive entries whose labels are at most 4
  characters and values at most 8 is laid out as a `Row` of equal cells, label above value,
  wrapping to a second row after 6 cells.
- The ink frame from feature 34 is kept, so a stat block still reads as one.

Item block:

- A hairline frame with a 2 pt ink top rule, so it doesn't read as a prop (props stay
  parchment).
- The name in Sans SemiBold 10.5 pt and the subtitle in italics.
- The entries as a two-column table, as now.
- A hairline, then the description in Serif.

Keep-together is feature 34's rule, measured on the block's total lines:
`KeepWholeMaxStatLines` (24) for both kinds. An unnamed block with no groups prints exactly as
feature 34 prints it.

## Export to Nornis

`AdventureExportComposer.BuildBody` gains a `gameSystem` argument. The export service resolves
it through `AdventurePrintDetails`, the same resolution the booklet uses. While writing a
section's body, the composer replaces each **named** game block's fence with one paragraph:

```
Stat block for "Captain Voss" (Shadowdark): *Medium human, pirate captain*. AC: 15; HP: 27 (5d8+5); Speed: 30 ft., swim 20 ft.; STR: 14; DEX: 16; CON: 12. Traits — Sea legs: Advantage on saves… Actions — Cutlass: +5 to hit, 1d8+3 slashing.
Item "Cloak of the Tidecaller" (Shadowdark): *Wondrous item, rare, requires attunement*. Weight: 1 lb; Value: 400 gp. While you wear this cloak…
```

- `GameBlocks.ExportLineFor(GameBlock, string? gameSystem)` builds the line and says whether it was cut. It is the one
  function both the composer and the warning use.
- A line over `MaxExportLineChars` (2,000, the `ArtifactFact.Value` column) is cut at the last
  whole entry or group that fits, and ends with " …".
- An unnamed block is left as its fence, so the extractor reads its lines as today.

The extraction prompt's Prep paragraph (`ExtractionPromptBuilder`) changes from "its lines may
become facts on that artifact" to:

> A line that starts `Stat block for "Name"` or `Item "Name"` is one fact on the Artifact it
> names: a Character for a stat block (a creature or person), an Item for an item. Use the
> predicate `stat block` or `item details`, and the rest of the line as the value, unchanged.
> Don't split it into separate facts.

## Pre-implementation checks

1. **Where each rule lives.**
   - The grammar lives in `GameBlocks.Parse`.
   - The export line lives in `GameBlocks.ExportLineFor`.
   - The prompt conventions live in `PrepMarkup.ConventionsForPrompt`.
   - The look lives in the renderer.
   - The info strings are mirrored to Web under the existing mirror test.
   - A boundary scan fails if anything outside `GameBlocks` matches on `StatBlockInfo` or
     `ItemBlockInfo` to parse a fence. The Web toolbar and the mirror are allowed.
2. **Second caller.** The booklet, the warnings and the export all call the parser. The
   warnings and the cut share `ExportLineFor`. Every section response path gets warnings,
   because they are computed when the response is built, not on a save.
3. **Null, empty and zero.**
   - A null name means unnamed: it prints as today, exports as lines, and gets a warning.
   - An empty subtitle is no subtitle.
   - A group heading with no lines prints as a heading only, which is harmless.
   - An empty block prints nothing and gets a warning.
   - A null game system leaves out the bracket.
4. **Unbounded.**
   - A block is bounded by `MaxSectionChars` (20,000).
   - The export line is bounded by `MaxExportLineChars`.
   - Warnings are bounded by the number of blocks in a section, which `MaxSectionChars`
     bounds.
   - The score row wraps after 6 cells.
5. **Shown failing.** Each guard below gets a sabotage pass before its PR.

## Testing

- **`GameBlocksTests`** cover:
  - every row of the grammar table;
  - the legacy `Name:` entry;
  - `##` and `---` in the wrong kind;
  - a colon in a description;
  - blank lines;
  - an empty block;
  - `ExportLineFor` at and over the limit, with the cut on a whole entry;
  - the game system's bracket present and absent.
- **`GameBlockWarningsTests`** cover each warning, positional naming, and none for a good block.
- **`PrepMarkupMirrorTests`** pick up `ItemBlockInfo` with no change to the test.
- **`PromptConventionsTests`** check that both prompts contain `ConventionsForPrompt`
  verbatim.
- **`AdventureExportComposerTests`** check that a named block becomes one line, an unnamed one
  stays a fence, and a cut line ends with " …".
- **`ExtractionPromptBuilder` test**: the Prep paragraph names both line shapes.
- **`QuestPdfBookletRendererTests`** (PdfPig) check that:
  - the name sits above the first entry;
  - a score row's six labels share one baseline;
  - an item card stays whole near a column's foot;
  - a group heading never ends a column.
- **`PrepBoundaryTests`**: only `GameBlocks` parses a game fence.
- **Web (bUnit)**: the Item button inserts the item template; warnings show under a section and
  disappear when the list is empty.
- **API**: a section response carries `blockWarnings`.
