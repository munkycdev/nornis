# Design Document

## Overview

A character detail view at `/characters/{characterId:guid}` composed of three sections, each
with a different relationship to truth:

| Section       | Where it comes from                            | Who owns it |
| ------------- | ---------------------------------------------- | ----------- |
| **Record**    | Derived at read time from the linked artifact  | The sources |
| **Sheet**     | One free-form text block, authored             | The player  |
| **Snapshots** | Existing sources attached with an as-of date   | The ledger  |

The whole design turns on keeping those three apart. The record is provenanced and cannot be
edited here; the sheet is authored and carries no provenance at all; the snapshots are sources
and obey the source rules. Blurring any two of them is how this feature would go wrong — a
sheet whose contents reached the Loremaster would let unprovenanced text be cited as though it
were canon, which is the one thing the product promises never happens.

## The five pre-implementation checks

### 1. Where does this rule live?

**Visibility of record material lives in `ArtifactService.GetDetailAsync`, and this feature
does not get its own copy.** The dossier calls it with the reader's role and groups what comes
back. It never queries `IArtifactFactRepository` or `IArtifactRelationshipRepository` directly.
`ArtifactDetail` is already documented as "already visibility-filtered for the requesting
user's role", and `VisibilityFilter.ForRole` is the one place that decides what that means.

**Ownership of a character lives in `CharacterService.CheckOwnershipAsync`** — "the owning
member may manage their own; GMs may manage any". Editing the sheet and attaching a snapshot
are both governed by it. No second expression of "owner or GM" is introduced.

**Grouping by artifact type lives in one projector**, `CharacterRecordProjector`, called only
by `CharacterService`. It is presentation-shaped, so the temptation is to put it in the razor;
that is precisely the mistake the continuity score made, and no compiler spans that boundary.

### 2. What happens when there is a second caller?

`ArtifactDetail.PlayedBy` already walks artifact → characters. This feature walks character →
artifact, which is a field read, not a query — so there is no second traversal to keep in sync.

The real second-caller risk is the *sheet*. A free-form text blob attached to a domain entity
is exactly the sort of thing a later feature reaches for: the Loremaster's context assembler,
the world digest, the campaign recap, a search index. Requirement 3.8 forbids it, but a
requirement is not a guard. **The guard is that the sheet is not on the entity every context
assembler already loads**: `Character.Sheet` is `[NotMapped]`-adjacent in spirit but practically
enforced by keeping it out of every read model except `CharacterDossier`, and by the property's
own XML comment stating why. A future author who wants it in the digest must delete a sentence
that tells them not to.

### 3. What do null, zero and empty each mean here?

| Value | Means | Renders as |
| ----- | ----- | ---------- |
| `ArtifactId` null | Never linked | No record section |
| `ArtifactId` set, `GetDetailAsync` 404s for this reader | Linked, but the artifact is above the reader's visibility | **No record section — byte-identical to the row above** |
| `Sheet` null | Never written | "Start a sheet" |
| `Sheet` empty after trim | Deliberately cleared | Normalized to null on write, so this state does not persist |
| `SheetSharedWithParty` false | Owner and GMs only | No share indicator |
| Snapshots empty | None attached | "No snapshots yet" |

Row two is load-bearing. If an unreadable link renders differently from an absent link — an
error, a greyed section, a different empty string — the page tells a player that a `GMOnly`
Character artifact exists with their character's name on it. **Unlinked and unreadable must be
indistinguishable**, and that is a test, not a comment.

`SheetSharedWithParty` is deliberately a bool rather than a `VisibilityScope`. The enum has
three states and only two are meaningful for player-authored text: `Private` and `GMOnly` would
both have to mean "owner and GM", which is a sentinel meaning two things — the defect this
question exists to catch.

### 4. What is unbounded?

| Thing | Bound | Where |
| ----- | ----- | ----- |
| Sheet text | `MaxSheetChars = 20_000`, server-enforced, refused not truncated | `CharacterService` |
| Snapshots per character | `MaxSnapshots = 50`, attaching the 51st is refused with a message | `CharacterService` |
| Connected artifacts per group | `MaxPerGroup = 24`, remainder reachable via `/artifacts/{id}` | `CharacterRecordProjector` |
| Facts / relationships loaded | Bounded by `ArtifactService.GetDetailAsync`'s own limits | elsewhere, by that service |

Refusing rather than truncating is deliberate for the first two: a silently truncated sheet is
data loss on a field whose entire purpose is being the player's own record.

### 5. Show it failing before showing it passing

Three guards get inverted deliberately before they are trusted:

- Put a `GMOnly` fact on a linked artifact, read the dossier as the owning **player**, and watch
  the assertion fail when the fact is present. Ownership must not widen visibility (R2.4).
- Read another member's unshared sheet as a Player and watch a permissive implementation return
  it.
- Link a character to a `GMOnly` artifact, read as a Player, and diff the response against the
  unlinked case. Any difference at all is the leak.

## Schema

Two additive changes and one new entity.

```csharp
// Character — additive
public string? Sheet { get; set; }              // free-form, uninterpreted, never AI-visible
public bool SheetSharedWithParty { get; set; }  // false = owner + GM
public DateTimeOffset? SheetUpdatedAt { get; set; }
```

```csharp
public class CharacterSheetSnapshot
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public Guid SourceId { get; set; }
    public DateTimeOffset AsOf { get; set; }        // what the sheet was current as of
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }

    public Character Character { get; set; } = null!;
    public Source Source { get; set; } = null!;
}
```

`(CharacterId, SourceId)` is unique — attaching the same source twice is the same statement.

**Cascade paths.** `World → Character → Snapshot` and `World → Source → Snapshot` are two
cascade paths between the same two tables, which SQL Server refuses — the same constraint that
forced `World.CurrentCampaignId` to `Restrict`. Resolution:

- `Snapshot → Source` is **Cascade**, which satisfies Requirement 4.7 declaratively: deleting a
  source cannot leave a dangling attachment.
- `Snapshot → Character` is **NoAction**, with `CharacterRepository.DeleteAsync` removing the
  rows explicitly — exactly the precedent `CampaignRepository.DeleteAsync` already sets for
  detaching what it cannot cascade.

## Read model

```csharp
public record CharacterDossier(
    Character Character,
    string OwnerDisplayName,
    IReadOnlyList<string> CampaignNames,
    CharacterRecord? Record,          // null = unlinked OR unreadable; see check 3
    string? Sheet,                    // null when absent or not readable by this reader
    bool SheetSharedWithParty,
    bool CanEditSheet,
    IReadOnlyList<CharacterSnapshotView> Snapshots);

public record CharacterRecord(
    Guid ArtifactId,
    IReadOnlyList<ArtifactFact> Facts,
    IReadOnlyList<CharacterRecordGroup> Groups);

public record CharacterRecordGroup(
    ArtifactType Type,
    IReadOnlyList<Artifact> Artifacts,
    int TotalCount);                  // > Artifacts.Count when capped at MaxPerGroup
```

`TotalCount` exists so the UI can say "24 of 31" honestly rather than implying the list is
complete. It counts only what the reader may see, so it cannot become a count of hidden
material — the mistake feature 22's Requirement 2 exists to prevent.

## Service and API

`CharacterService` gains, all reusing `CheckOwnershipAsync`:

- `GetDossierAsync(characterId, worldId, actingUserId, role, ct)` → `CharacterDossier`
- `UpdateSheetAsync(...)` — validates length, normalizes empty to null, stamps `SheetUpdatedAt`
- `SetSheetSharingAsync(...)` — owner only; a GM may read but does not decide who else does
- `AttachSnapshotAsync(...)` / `DetachSnapshotAsync(...)` — validates the source is in the same
  world and readable by the actor, and enforces `MaxSnapshots`

`CharactersController` gains the matching routes under the existing character resource. No new
controller: characters already have one.

**`GetDossierAsync` composes rather than queries.** It reads the `Character`, then — only when
`ArtifactId` is set — calls `ArtifactService.GetDetailAsync` and projects. A 404 or 403 from
that call is swallowed into `Record: null`, which is the indistinguishability requirement
expressed in code.

## UI

`Components/Pages/CharacterDetail.razor`, MudBlazor, reusing `NotesEditor` for sheet editing and
`MarkdownRenderer` for display — both already exist and already handle the sanitization
question, so the sheet does not get a third answer to it.

Entry points: the character rows on `Profile.razor` become links, and `ArtifactDetail`'s
`PlayedBy` names become links back. Those two are the whole navigation story; the feature adds
no nav entry, because a character is reached through a person or an artifact, not browsed.

## Correctness Properties

### Property 1: The record is read-only here

No mutation of any artifact, fact, or relationship originates from this view. Changing what the
record says is what sources and review are for.

### Property 2: Ownership does not widen visibility

The reader's `VisibilityFilter.ForRole` governs the record section regardless of whether they
own the character. A player who owns Tavrin sees exactly what any other player sees about the
Tavrin artifact.

### Property 3: Unlinked and unreadable are indistinguishable

A response for a character linked to an artifact above the reader's visibility is byte-identical
to one for an unlinked character.

### Property 4: The sheet never reaches an AI path

No prompt assembly, context builder, digest, recap, or embedding input reads `Character.Sheet`.
Unprovenanced text must not be citable.

### Property 5: One visibility rule per artefact

Snapshot readability is the source's own. The feature adds no second rule for the same bytes,
and detaching never deletes.

### Property 6: Nothing is computed

No arithmetic on, validation of, or system-aware interpretation of any value in the sheet.

## Error Handling

- Character not found, or in another world → 404, no distinction between the two.
- Sheet edit by a non-owning, non-GM member → 403 via the existing ownership error.
- Sheet over `MaxSheetChars` → 400 naming the limit; nothing is saved.
- Snapshot source in another world, or unreadable by the actor → the same 400 for both, so the
  error does not confirm a source id exists.
- Snapshot beyond `MaxSnapshots` → 400 naming the limit.

## Testing

NUnit, per `testing-strategy.md`, with `CharacterServiceTests` extended rather than a parallel
suite. Authorization and visibility are priority 1 in that document and are most of what is
worth testing here:

- The three inverted guards from check 5 above.
- Owner, GM, other Player, and Observer against: reading the dossier, reading the sheet, editing
  the sheet, sharing the sheet, attaching a snapshot.
- Both null/empty sheet states normalizing to one.
- `MaxSheetChars` and `MaxSnapshots` refusing rather than truncating.
- Grouping caps reporting `TotalCount` above `Artifacts.Count`.
- `CharacterRepository.DeleteAsync` removing snapshot rows, since no cascade will.

## Design decisions to confirm before build

1. **`MaxSheetChars = 20_000`** — generous for text, short of a document. Adjustable, but a
   number is needed before Phase B.
2. **Phase D is genuinely optional.** If the name-presence test proves noisy in practice it
   should be dropped rather than tuned toward cleverness; tuning it is how a text search becomes
   a parser.
3. **Snapshots attach existing sources rather than uploading.** Upload lives in one place today
   and this feature does not become a second one — at the cost of a two-step flow the first time.
