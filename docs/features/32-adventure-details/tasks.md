# Tasks

Three PRs: the spec, then storage and the Details card (the only migration, additive), then
everything that reads the details, and the version bump. Ships as Nornis v3.4. Feature 33 (PR
#82) is labelled 3.3; Phase B is generated after it lands.

**Status: specified 2026-11-03; Phase A done; Phase B shipped 2026-11-03 (PR #87; the card is "Book details", see the design amendment); Phase C built 2026-11-03, shipping as 3.4. C7, the live walk, is Dave's. Type, publisher and author fixed by Dave, 2026-11-03.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 32 in the features README.
- [x] A2. `domain-model.md` amendment: `AdventureDetails`, `Adventure.Details`,
      `LibraryDocument.PrintDetails`.
- [x] A3. Feature 31's contract document gains the optional `book` block on a provider file.
- [x] A4. The Bibliotaph-side note, as a shareable doc for Dave's Bibliotaph project:
      "Bibliotaph: book details from Nornis prints",
      https://claude.ai/artifact/7EVVtGRDTyjxupVJpqZmtR. The contract amendment is in the
      file-listing row of https://claude.ai/artifact/TjE2qTGmB1WcBZV4GxcKYB.

## Phase B — Storage, the route and the Details card

- [x] B1. `AdventureDetails` (Domain): the four editable fields, the bounds, `Validate`,
      normalisation. `AdventureDetailsTests`. Sabotage: each bound.
- [x] B2. `PrintDetails` (Domain) with `Resolve` and the fixed values (`NornisType`,
      `NornisPublisher`, the creator's username); `Adventure.Details`;
      `LibraryDocument.PrintDetails`. `PrintDetailsTests`.
- [x] B3. EF owned-type configuration for both; migration `AddAdventureDetails`, additive
      (twelve nullable columns). Persistence round-trip tests. Apply it to
      production by hand before B merges.
- [x] B4. `AdventureService.UpdateDetailsAsync`, GM first, nothing saved on a bad field.
- [x] B5. `PUT prep/{id}/details`; `AdventureResponse` gains `details` and `printDetails`;
      the route in the 403 sweep.
- [x] B6. Web: the Details card, field by field; the mirror test for its bounds and default
      type; bUnit tests.

## Phase C — The booklet, the print snapshot, Bibliotaph, import, and ship 3.4

- [x] C1. `AdventurePrintModel`: cover lines and the credits line. The print page's title is
      the Adventure's alone.
- [x] C2. `RequestUploadAsync` snapshots `PrintDetails`. `LibraryPrintTests`;
      `PrepBoundaryTests` pins the one writer.
- [x] C3. The provider's `book` block. `BibliotaphProviderTests`.
- [x] C4. Import Markdown front matter and `ignoredDetails`. Importer tests.
- [x] C5. `prep.json` and the Library export carry the details.
- [x] C6. Ship: `MinorVersion` 4, changelog entry, Features page, README, feature README row,
      tasks ticks, steering re-check notes.
- [ ] C7. Live walk: fill the details on the Gribbits adventure, print, file to the Library,
      see it in Bibliotaph (described exactly once Bibliotaph reads `book`; by its suggester
      before that).

## Steering re-check (C6)

- `pre-implementation-checks.md`: one resolver (`PrintDetails.Resolve`) and one lookup
  (`AdventurePrintDetails`) for the preview, the print page and the snapshot; one writer of a
  print's details, pinned by `PrepBoundaryTests`; every string bounded, the front matter scan
  stops at 50 lines and reports at most one key per line; each guard sabotage-checked.
- `security-and-permissions.md`: the details route is GM-only by current role and in the 403
  sweep. The provider's `book` block is on files Bibliotaph could already list; it adds no
  file and no world.
- `domain-model.md`: the 2026-11-03 amendment stands as built.
- `ai-extraction.md`: details are never extracted and never read by Ask (unchanged paths).

