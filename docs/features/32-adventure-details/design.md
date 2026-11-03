# Design Document

## Overview

```
GM ── Details card ── PUT prep/{id}/details ──▶ AdventureService.UpdateDetailsAsync
                                                  └─ AdventureDetails.Validate (the bounds)
                                                  └─ Adventure.Details (owned, 4 columns)

Print page ── GET prep/{id}/print ──▶ AdventurePrintModel.Render(adventure, PrintDetails.Resolve(…))
File to Library ── LibraryService.RequestUploadAsync (AdventureId set)
                    └─ document.PrintDetails = PrintDetails.Resolve(…)   (owned, 8 columns)

Bibliotaph ── GET api/bibliotaph/v1/worlds/{w}/files ──▶ BibliotaphProviderFile { …, book? }
```

## Two types, one resolver

Dave fixed three fields on 2026-11-03: type is "Nornis adventure", publisher is "Nornis
Press", and the author is the creator's username. So what the GM stores and what a print
carries are different shapes, and each gets its own record in `Nornis.Domain.Models`:

- **`AdventureDetails`** holds what the GM edits: `GameSystem`, `Edition`, `Levels` and
  `Year`, all nullable. It is stored on the Adventure.
- **`PrintDetails`** holds what a print says: `Title`, `GameSystem?`, `Edition?`, `Type`,
  `Levels?`, `Publisher`, `Author` and `Year?`. It is resolved and complete, stored on a
  Library document, and sent to Bibliotaph.

One static method owns every default, so no caller restates one (pre-implementation check 1):

```csharp
PrintDetails.Resolve(string title, AdventureDetails details, string? worldGameSystem, string authorUsername)
```

It sets `GameSystem = details.GameSystem ?? worldGameSystem`, `Type = PrintDetails.NornisType`
("Nornis adventure"), `Publisher = PrintDetails.NornisPublisher` ("Nornis Press") and
`Author = authorUsername`. Three callers use it:

- the print page;
- the Details card's display (through the API, which returns the resolved preview);
- the print snapshot.

`AdventureDetails.Validate()` returns the first field that breaks a bound, or null. The service
refuses with 400 `invalid_details`; the importer drops the field instead.

Type and publisher are stored on each print even though they are constants today. A print is a
snapshot (Requirement 4.2): if the wording ever changes, booklets already filed keep theirs.
The same holds for the author: a later username change doesn't reach an old print.

### Null, empty and zero (check 3)

| Field | Null on an Adventure | On a print |
| ----- | -------------------- | ---------- |
| GameSystem | the world's game system | null = not set (the world had none either) |
| Edition, Levels | not set | null = not set |
| Year | not set (0 is refused) | null = not set |
| Type, Publisher, Author | — (not stored per Adventure) | never null |

Every string is trimmed, and a trimmed empty string becomes null before validation, so
"cleared" and "never set" are the same state.

### Bounds (check 4)

| Field | Bound |
| ----- | ----- |
| GameSystem | 200 chars, the same as `World.GameSystem` |
| Edition | 50 chars |
| Levels | 30 chars (free text: "1–3", "3rd", "any") |
| Year | 1900 to the current year + 5 |
| Print `Title` | 200 chars, the same as `Adventure.Title` |
| Print `Author` | the `User.Username` column's length |

The bounds live as constants on `AdventureDetails`. The Web mirrors them for its field
`MaxLength`s, pinned by a mirror test that reads the Domain source, like the conversation's.

## Persistence

- `Adventure.Details` is an EF **owned type** on `Adventures`. It adds four columns,
  `Details_GameSystem`, `Details_Edition`, `Details_Levels` and `Details_Year`.
- `LibraryDocument.PrintDetails` is an optional owned `PrintDetails` on `LibraryDocuments`. It
  adds eight columns, `PrintDetails_Title` and so on. A document that is not a print has all
  eight null, and EF reads that as no print details. `Title`, `Type`, `Publisher` and `Author`
  are required inside the owned type, so a half-written snapshot can't load as a print.
- One migration, `AddAdventureDetails`, adds the twelve nullable columns. It is additive,
  existing rows read as "no details", and nothing is backfilled.

Feature 33's `AddUserSignIns` (PR #82) may land first. Phase B is generated against whatever
`main` holds then, so the snapshot stays linear.

## API

```
PUT  api/worlds/{w}/prep/{id}/details   GM {gameSystem?, edition?, levels?, year?} → AdventureResponse
```

- The request is the whole editable set: each field is the new value, and null clears it. The
  Details card sends all four, after changing one. Two tabs editing different fields can
  overwrite each other's change. Dave is the only GM, and the same is true of the title and
  pitch today, so there is no stamp.
- `AdventureResponse` gains two members. `details` is the stored four. `printDetails` is the
  resolved preview, all eight, as a print filed now would carry them. The card shows the
  preview, so it never computes a default itself.
- The route joins `PrepEndpointTests.Routes()`, so the 403 sweep covers it.
- Import's result gains `ignoredDetails`: the front matter keys that were dropped.

## Web

- A **Details** card on the Adventure page, under the pitch in the left column. It has one
  row per field, in Bibliotaph's order: title, game system, edition, type, levels, publisher,
  authors, year.
  - Game system, edition, levels and year each have a pencil. It turns the row into a text
    field with Save and Cancel. Enter saves and Escape cancels.
  - Game system shows the world's value with the caption "From the world" when unset.
  - Type, publisher and authors show their fixed values with no pencil, and the caption "Set
    by Nornis".
  - The title row shows the Adventure's title. It is edited where it is today, so it gets no
    pencil here.
- The print page's `<PageTitle>` becomes the Adventure's title alone (Requirement 3.3).

## The booklet

`AdventurePrintModel.Render(adventure, printDetails)` takes the resolved details.

- **Cover**, under the title and before the pitch:
  - `{GameSystem} {Edition}`, when the game system is set;
  - `Levels {Levels}`, when set;
  - `By {Author}`.
- **Credits**: one line at the foot of the cover, `{Type} · {Publisher} · {Year}`, with the
  year left out when unset.
- Every value is HTML-encoded, as titles are.

## The print snapshot

`LibraryService.RequestUploadAsync` already loads `printedFrom` and sets `AdventureId`. In the
same object initialiser it also sets `PrintDetails = PrintDetails.Resolve(printedFrom.Title,
printedFrom.Details, world.GameSystem, creator.Username)`, so the snapshot is set in one place.
That place is the one `PrepBoundaryTests` already pins for `AdventureId`. A new scan in that
fixture pins that `PrintDetails` is assigned nowhere else. That is the "never changes after
filing" guard (Requirement 4.2): there is no second writer to forget.

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
    string Title, string? GameSystem, string? Edition, string Type, string? Levels,
    string Publisher, IReadOnlyList<string> Authors, int? Year);
```

- `Book` is set when the document has print details. `Title` is the Adventure's title when the
  booklet was filed. That is the Library title without its ", printed yyyy-mm-dd" suffix, and
  a later rename of the Adventure doesn't reach it.
- `Authors` is a list, the shape Bibliotaph's field takes, holding the one author.
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
- never overwrite a field the user has since edited in Bibliotaph;
- if Type is a fixed list there, it needs a "Nornis adventure" entry.

The note is published with this spec as a shareable doc.

## Import Markdown

`AdventureMarkdownImporter` gains a front matter step before the heading split, on the first
file only.

- The block opens with `---` on line 1 and closes with the next `---` line, within 50 lines. A
  file whose first line is `---` but has no close within 50 lines is treated as having no
  front matter, and the line stays in the text, as today.
- The `system` (or `game_system`), `edition`, `levels` and `year` lines map to the fields.
  Keys are matched ignoring case, `-` and `_`. Any other key is reported in `ignoredDetails`,
  including `type`, `publisher` and `authors`, which are fixed.
- Values go through the same `AdventureDetails.Validate`. A field that fails is dropped and
  reported, and the import never fails over its front matter (Requirement 6.4).
- No YAML library is added. The block is a flat key/value list, and a real YAML parser would
  accept far more than the importer maps.

## Export and deletion

- `prep.json` gains each Adventure's `details`.
- The Library section of the export gains each document's `printDetails`, where present.
- Deletion needs nothing new: both column sets live on rows already deleted with their
  Adventure, document or world.

## Pre-implementation checks

1. **Where does the rule live?** The defaults and the three fixed values live in
   `PrintDetails.Resolve` and its constants. The bounds live on `AdventureDetails`. The
   snapshot is set in `RequestUploadAsync`. The Web's `MaxLength`s are mirrors, pinned by a
   test.
2. **A second caller?** The importer and the details route both validate through `Validate()`.
   The print page, the card's preview and the snapshot all resolve through `Resolve`.
3. **Null, zero, empty:** see the table above. Empty strings normalise to null, and year 0 is
   refused.
4. **Unbounded?** Every string is capped, and the front matter scan stops at 50 lines.
5. **Failing first:** each guard is sabotage-checked:
   - the snapshot written twice;
   - a fixed value spelt twice;
   - a bound removed;
   - the block parsed from the second file.

## Testing

- `AdventureDetailsTests`:
  - every bound, refused at the bound plus one;
  - trimming, and empty becoming null.
- `PrintDetailsTests`:
  - the game system falls back to the world's;
  - type, publisher and author are the fixed values;
  - unset fields stay null.
- `AdventureServiceTests`:
  - a GM saves details;
  - a Player or an Observer gets 403;
  - one bad field saves nothing.
- `PrepEndpointTests`: the route in the 403 sweep, a round trip with the preview, and 400
  `invalid_details`.
- Persistence (SQLite):
  - both owned types round-trip;
  - a document that is not a print reads back with null print details.
- `LibraryPrintTests`:
  - a print's details are resolved at filing;
  - renaming the Adventure or changing its details afterwards leaves them as filed;
  - an upload that is not a print has none.
- `PrepBoundaryTests`: `PrintDetails` is assigned only in `RequestUploadAsync`.
- `BibliotaphProviderTests`:
  - a print carries `book` with the printed title and one author;
  - an upload has no `book`;
  - the etag is unchanged by details.
- `AdventurePrintModelTests`:
  - the cover lines, each only when set;
  - the credits line;
  - encoding.
- `AdventureMarkdownImporterTests`:
  - front matter is read and kept out of the bodies;
  - `type`, `publisher` and `authors` are ignored and reported;
  - an unclosed block;
  - a bad year is dropped and reported;
  - a second file's block is not read;
  - no front matter is unchanged.
- bUnit:
  - the card shows "Not set", "From the world" and "Set by Nornis";
  - only four rows have a pencil;
  - editing one field sends all four with only that one changed;
  - the print page's title.
- Mirror test: the Web's `MaxLength`s match the Domain's.

## Open question

- **Levels format.** Nornis sends the text as typed ("1–3"). If Bibliotaph stores a range as
  two numbers, its side parses it.
