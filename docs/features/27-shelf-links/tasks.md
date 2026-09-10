# Tasks

Ordered so the resolve path and the GM gate are proven before any page reads them. One
migration, additive.

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

- [ ] V1. Build, `dotnet test --solution Nornis.sln`, `dotnet format --verify-no-changes`, in
      the worktree.
- [ ] V2. Migration applied to production; `SELECT COUNT(*) FROM ShelfLinks` answers 0.
- [ ] V3. Live: mint, open in a private window, download, revoke, reload.

## Deferred (explicitly not this feature)

- The link opening the whole party-visible read surface (codex, sessions, timeline).
- Expiry on a link.
- A per-document "not through links" flag — the GM shelf is that today.
- Links for members.
