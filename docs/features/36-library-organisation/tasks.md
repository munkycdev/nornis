# Tasks

Two PRs: the spec (with features 37 and 38), then the build. Ships as Nornis 3.7. One
migration, which Dave applies before the merge.

**Status: specified 2026-11-03. Dave agreed to the shape in the Prep thread: categories with
filter chips, and an optional "belongs to an adventure" link that keeps one-shot material off
the Shelf.** **Built 2026-11-03 (B1 to B5); see the amendment at the top of `design.md`. B6
is Dave's.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 36 in the features README.

## Phase B — Build

- [x] B1. `LinkedAdventureId` on `LibraryDocument`, its configuration (SetNull, index), and the
      migration `AddLibraryDocumentAdventureLink`. SQL test for SetNull.
- [x] B2. `LibraryCategory.Of` and `IsOnShelf`; `SetAdventureLinkAsync` and `SetKindAsync`
      with their refusals; the boundary scan for the link's one writer. Tests.
- [x] B3. API: `category`, `onShelf`, `linkedAdventure` (GM only) on library rows; the link and
      kind endpoints; the upload's optional link; `documents` on the Adventure. Tests,
      including a player's view.
- [x] B4. Web: chips with counts, the Adventure menu, search and sort on the Library page; the
      document page's selects; the upload dialog's select; the Adventure page's Documents
      card. bUnit tests.
- [x] B5. Ship: `MinorVersion` 7; the change log, the Features page and the README; row 36;
      ticks here; the steering re-check. Validate: a clean worktree build with 0 warnings,
      the full test run, `dotnet format`, and sabotage of each new guard.
- [ ] B6. Live walk (Dave): apply the migration, link a handout to the Slurping Spa, see it
      leave the Shelf and appear under the adventure's filter and on its page.
