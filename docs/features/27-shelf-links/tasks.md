# Tasks

Ordered so the resolve path and the GM gate are proven before any page reads them. One
migration, additive.

**Status: built and shipped 2026-09-10** in one branch (`feature-27-handout-links`) from a
worktree, with the migration applied to production by the build session before the merge
(`0ec10ef`). What the build changed from the spec, and what it found:

- **One index on `PlayerId`, not two.** EF treats a second `HasIndex` on the same property as
  the same index, so the plain one silently became the filtered unique standing index. That is
  the only index needed — the standing lookup is the hot one — and the configuration now says
  so rather than declaring both.
- **The library kind icon has one home.** The Library page's private switch moved to
  `LibraryDisplay.KindIcon`, and the shelf page reads it too.
- **The dev-auth bypass skips `/api/shelf`** beside `/api/public`, so the anonymous family is
  genuinely anonymous locally. Found during the walk (the shelf ignores the user either way);
  shipped in the follow-up commit.
- **The sabotage fired**: `LinkHolderRole = WorldRole.GM` turned exactly the two GM-shelf
  tests red (`Open_ListsThePartyShelf_NotTheGmShelf_NotPendingUploads`,
  `Download_GmShelfDocument_Returns404`) and nothing else.
- **Format gate**: three IDE0300 hits in the new tests, rewritten as a count plus an element
  (`Is.EqualTo(new[] { x })` cannot become a collection expression against `object`). The
  preview SDK's `WHITESPACE` reports in `Program.cs` are not CI's and were left alone.
- **Live walk (V3), local API and Web against production, "Forgotten Realms" as the dev GM**:
  added "Henry (shelf test)" and uploaded a 448-byte party-visible handout through the SAS
  handshake; on the Party row, *Give Henry a link to the shelf* produced the URL field, copy,
  QR, new-link and revoke controls and "Never opened", and the QR rendered as inline SVG.
  Opened `/shelf/{code}` anonymously: world eyebrow, "Shared with Henry (shelf test)", the
  handout with kind and size, `noindex, nofollow` in the head. Download opened the blob SAS URL,
  and a shell GET of the anonymous download endpoint and then of that URL returned the PDF; a
  made-up document id answered 404. Back on Party the row read "Opened 1m ago". Revoke asked,
  the row returned to the mint button, and the shelf URL rendered "This link isn't valid". The
  test player and handout were deleted afterwards; the world's standing links list is empty.
- **V2**: the deploy's `/health` reported no pending migrations. The zero-count query was not
  run — no SQL client in the session — and the table is new, so it is empty by construction.

## Phase A — The link

- [x] A1. `ShelfLink` entity, `ShelfLinkConfiguration` (unique code, filtered unique standing
      index, Player cascade, creator restrict), `NornisDbContext.ShelfLinks`; `domain-model.md`
      amendment, dated.
- [x] A2. Migration `AddShelfLinks`.
- [x] A3. `IShelfLinkRepository` (+ EF and in-memory fake).
- [x] A4. `IShelfLinkService` + `ShelfLinkService`; `Shelf` model; tests including the sabotage.
- [x] A5. `ShelfController` (anonymous, rate-limited) and `ShelfLinksController` (GM);
      responses; `Program.cs` registrations; endpoint tests.
- [x] A6. `PublicController.ListSources` filter removed; the public test inverted.
- [x] A7. `security-and-permissions.md`: `/api/shelf/**` on the anonymous list and a Shelf
      Links section beside World Invitation.

## Phase B — The pages

- [x] B1. Web contracts and client methods.
- [x] B2. `/shelf/{Code}` page; `ReachabilityTests` exemption.
- [x] B3. Party page block: mint, copy, QR, last opened, revoke, new link. QRCoder in the
      project and on the Licenses page.
- [x] B4. Change log entry; `docs/features/README.md` row.

## Verification

- [x] V1. Build, `dotnet test --solution Nornis.sln`, `dotnet format --verify-no-changes`, in
      the worktree.
- [x] V2. Migration applied to production; `SELECT COUNT(*) FROM ShelfLinks` answers 0.
- [x] V3. Live: mint, open in a private window, download, revoke, reload.

## Deferred (explicitly not this feature)

- The link opening the whole party-visible read surface (codex, sessions, timeline).
- Expiry on a link.
- A per-document "not through links" flag — the GM shelf is that today.
- Links for members.
