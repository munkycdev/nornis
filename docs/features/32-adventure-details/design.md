# Design Document

## Overview

```
GM ── Details card ── PUT prep/{id}/details ──▶ AdventureService.UpdateDetailsAsync
                                                  └─ AdventureDetails.Validate (the bounds)
                                                  └─ Adventure.Details (owned, 7 columns)

Print page ── GET prep/{id}/print ──▶ AdventurePrintModel.Render(adventure, resolved details)
File to Library ── LibraryService.RequestUploadAsync (AdventureId set)
                    └─ document.PrintDetails = AdventureDetails.ResolveForPrint(adventure, world)

Bibliotaph ── GET api/bibliotaph/v1/worlds/{w}/files ──▶ BibliotaphProviderFile { …, book? }
```

## The one type: `AdventureDetails`

There is one value object, `Nornis.Domain.Models.AdventureDetails`, a record with
`GameSystem`, `Edition`, `Type`, `Levels`, `Publisher` and `Year` (all nullable), and
`Authors` (a list that is never null). It is used three ways:

- as an Adventure's **stored** details (`Adventure.Details`), where a null `GameSystem` or
  `Type` means "the default";
- as a print's **print details** (`LibraryDocument.PrintDetails`), always resolved, where
  null means only "not set";
- as the shape the API and the provider send.

Two methods own its two rules, so no caller restates them (pre-implementation check 1):

- `Validate()` returns the first field that breaks a bound, or null. The service refuses with
  400 `invalid_details`. The importer drops the field instead.
- `ResolveForPrint(string? worldGameSystem)` returns a copy with `GameSystem ??=
  worldGameSystem` and `Type ??= "Adventure"`. The print page, the Details card's display,
  and the print snapshot all call it. `AdventureDetails.DefaultType` is the one spelling of
  "Adventure".

### Null, empty and zero (check 3)

| Field | Null on an Adventure | Null on a print | Empty |
| ----- | -------------------- | --------------- | ----- |
| GameSystem | the world's game system | not set (the world had none either) | normalised to null |
| Type | "Adventure" | never null after resolving | normalised to null |
| Edition, Levels, Publisher | not set | not set | normalised to null |
| Authors | — (never null) | — | empty list = none; blank names dropped |
| Year | not set | not set | — (0 is refused) |

Every string is trimmed, and a trimmed empty string becomes null before validation, so
"cleared" and "never set" are the same state.

### Bounds (check 4)

| Field | Bound |
| ----- | ----- |
| GameSystem | 200 chars, the same as `World.GameSystem` |
| Edition | 50 chars |
| Type | 50 chars |
| Levels | 30 chars (free text: "1–3", "3rd", "any") |
| Publisher | 200 chars |
| Authors | at most 10 names, each at most 100 chars |
| Year | 1900 to the current year + 5 |

The bounds live as constants on `AdventureDetails`. The Web mirrors them for its field
`MaxLength`s, pinned by a mirror test that reads the Domain source, like the conversation's.

## Persistence

- `Adventure.Details` is an EF **owned type** on `Adventures`. That gives seven columns,
  `Details_GameSystem` and so on, and `Details_Authors` holds the list as JSON in
  `nvarchar(max)`, with a value comparer.
- `LibraryDocument.PrintDetails` is an optional owned `PrintDetails` record on
  `LibraryDocuments`: `Title`, the Adventure's title as printed, plus the resolved
  `AdventureDetails` nested inside it. A document that is not a print has all eight columns
  null, and EF reads that as no print details.
- One migration, `AddAdventureDetails`, adds the fifteen nullable columns. It is additive,
  existing rows read as "no details", and nothing is backfilled.

Feature 33's `AddUserSignIns` (PR #82) may land first. Phase B is generated against whatever
`main` holds then, so the snapshot stays linear.

## API

```
PUT  api/worlds/{w}/prep/{id}/details   GM {gameSystem?, edition?, type?, levels?, publisher?, authors[], year?} → AdventureResponse
```

- The request is the whole set: each field is the new value, and null clears it. The Details
  card sends all seven, after changing one. Two tabs editing different fields can overwrite
  each other's change. Dave is the only GM, and the same is true of the title and pitch
  today, so there is no stamp.
- `AdventureResponse` gains `details` (stored) and `printDetails` (resolved), so the card
  can show "from the world" or "default" without knowing the rule.
- The route joins `PrepEndpointTests.Routes()`, so the 403 sweep covers it.
- `ImportAdventureResponse` (or the import's existing result) gains `ignoredDetails`: the
  front matter keys that were dropped.

## Web

- A **Details** card on the Adventure page, under the pitch in the left column. It has one
  row per field: a label, the value or "Not set", and a pencil.
  - The pencil turns the row into a text field with Save and Cancel. Enter saves and Escape
    cancels.
  - Authors is edited as one comma-separated line, so the list never needs its own editor.
  - Game system and type show their resolved value with a caption, "From the world" or
    "Default", when unset.
  - Type offers suggestions (Adventure, One-shot, Campaign, Supplement, Sourcebook) and
    accepts anything.
- The print page's `<PageTitle>` becomes the Adventure's title alone (Requirement 3.3).

## The booklet

`AdventurePrintModel.Render(adventure, details)` takes the resolved details.

- **Cover**, under the title and before the pitch:
  - `{GameSystem} {Edition}`;
  - `Levels {Levels}`;
  - `By {Authors joined with ", " and " and "}`.

  Each line appears only when its field is set.
- **Credits**: one line at the foot of the cover, `{Type} · {Publisher} · {Year}`, with the
  unset parts left out.
- Every value is HTML-encoded, as titles are.

## The print snapshot

`LibraryService.RequestUploadAsync` already loads `printedFrom` and sets `AdventureId`. It
also sets `PrintDetails = new(printedFrom.Title, printedFrom.Details.ResolveForPrint(world.GameSystem))` in the same
object initialiser, so it is set in one place, the place `PrepBoundaryTests` already pins for
`AdventureId`. A new scan in that fixture pins that `PrintDetails` is assigned nowhere else.
That is the "never changes after filing" guard (Requirement 4.2): there is no second writer
to forget.

The snapshot is taken at request-upload, not at confirm. The two are seconds apart, the
confirm is the same GM's own upload finishing, and request-upload is where the print's other
fixed fields are set.

## The provider contract

`BibliotaphProviderFile` gains a last, optional member:

```csharp
public record BibliotaphProviderFile(
    Guid FileId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UpdatedAt, string Etag,
    BibliotaphProviderBook? Book);

public record BibliotaphProviderBook(
    string Title, string? GameSystem, string? Edition, string? Type, string? Levels,
    string? Publisher, IReadOnlyList<string> Authors, int? Year);
```

- `Book` is set when the document has print details. `Title` is `PrintDetails.Title`, the
  Adventure's title when the booklet was filed. That is the Library title without its
  ", printed yyyy-mm-dd" suffix, and a later rename of the Adventure doesn't reach it.
- The `Etag` is unchanged. Print details never change, so a file's etag still changes only
  when its bytes do.
- Null means "Nornis has no value". Bibliotaph may suggest that field as it does today.

The contract document (feature 31's, linked from its design.md) is amended first: the new
member, its meaning, and that an older Bibliotaph ignores it.

### The Bibliotaph side (for Dave's Bibliotaph project)

When the catalogue sync imports or refreshes a file from the Nornis storage provider and the
file has a `book` block:

- set each non-null field on the entry as a value the user gave, not a suggestion;
- leave a null field to the suggester, as today;
- never overwrite a field the user has since edited in Bibliotaph.

The note is published with this spec as a shareable doc.

## Import Markdown

`AdventureMarkdownImporter` gains a front matter step before the heading split, on the first
file only:

- The block opens with `---` on line 1 and closes with the next `---` line, within 50 lines.
  A file whose first line is `---` but has no close within 50 lines is treated as having no
  front matter: the line stays in the text, as today.
- `key: value` lines map to the fields. `authors` accepts a comma-separated value or the
  following `- name` lines. Keys are matched ignoring case, `-` and `_`.
- Values go through the same `AdventureDetails.Validate`. A field that fails is dropped and
  reported in `ignoredDetails`, and the import never fails over its front matter
  (Requirement 6.4).
- No YAML library is added. The block is a flat key/value list, and a real YAML parser would
  accept far more than the importer maps.

## Export and deletion

- `prep.json` gains each Adventure's `details`.
- The Library section of the export gains each document's `printDetails`, where present.
- Deletion needs nothing new: both column sets live on rows already deleted with their
  Adventure, document or world.

## Pre-implementation checks

1. **Where does the rule live?** Defaults and bounds are on `AdventureDetails`, the snapshot
   is set in `RequestUploadAsync`, and the Web's `MaxLength`s are mirrors pinned by a test.
2. **A second caller?** The importer and the details route both validate through
   `Validate()`. The print page, the card and the snapshot all resolve through
   `ResolveForPrint`.
3. **Null, zero, empty:** see the table above. Empty strings normalise to null, and year 0
   is refused.
4. **Unbounded?** Every string is capped, authors at 10, and the front matter scan at 50
   lines.
5. **Failing first:** each guard is sabotage-checked:
   - the snapshot written twice;
   - the default type misspelt in the Web;
   - a bound removed;
   - the block parsed from the second file.

## Testing

- `AdventureDetailsTests`:
  - every bound, refused at the bound plus one;
  - trimming and empty → null;
  - the resolve defaults;
  - authors that are blank, duplicated or over the cap.
- `AdventureServiceTests`: a GM saves details; a Player or Observer gets 403; one bad field
  saves nothing.
- `PrepEndpointTests`: the route in the 403 sweep, a round trip, and 400 `invalid_details`.
- Persistence (SQLite):
  - the owned columns round-trip, including the authors JSON;
  - a document that is not a print reads back with null print details.
- `LibraryPrintTests`:
  - a print's details are resolved at filing;
  - changing the Adventure afterwards leaves them as filed;
  - an upload that is not a print has none.
- `PrepBoundaryTests`: `PrintDetails` is assigned only in `RequestUploadAsync`.
- `BibliotaphProviderTests`:
  - a print carries `book` with the printed title;
  - an upload has no `book`;
  - the etag is unchanged by details.
- `AdventurePrintModelTests`:
  - the cover lines, each only when set;
  - the credits line;
  - encoding.
- `AdventureMarkdownImporterTests`:
  - front matter is read, and kept out of the bodies;
  - both authors forms;
  - an unclosed block;
  - a bad year is dropped and reported;
  - a second file's block is not read;
  - no front matter is unchanged.
- bUnit:
  - the card shows "Not set", "From the world" and "Default";
  - editing one field sends all seven with only that one changed;
  - clearing type returns it to Default;
  - the print page's title.
- Mirror test: the Web's `MaxLength`s and `DefaultType` match the Domain's.

## Open questions

- **Bibliotaph's type list.** If Bibliotaph's Type is a fixed list, the Details card's
  suggestions become that list and the Bibliotaph side maps any other value to its nearest.
  Until it's confirmed, Nornis sends what the GM typed.
- **Levels format.** Nornis sends the text as typed ("1–3"). If Bibliotaph stores a range as
  two numbers, its side parses it.
