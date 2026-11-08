# Features

One folder per feature, numbered in build order, each holding the spec that drove it:
`requirements.md` (what and why), `design.md` (how), and `tasks.md` (the build record — what
shipped, what was declined, what was deferred, and any place the build differed from the
spec). The specs are the record of intent; `tasks.md` is the record of what happened. Where a
`tasks.md` carries a **Status** paragraph, that paragraph is the authority and the checkboxes
under it are the original plan.

There is no feature 15. Nothing in the tree or the git history refers to one; the number was
simply never used, and it stays unused so that every existing reference keeps its meaning.

| # | Feature | Shipped | Left open, by design |
| --- | --- | --- | --- |
| 1 | Domain and data layer | 2026-06 | — |
| 2 | Project scaffolding | 2026-06 | — |
| 3 | Auth and campaigns (now: worlds and members) | 2026-07 | — |
| 4 | Campaign sources | 2026-07 | — |
| 5 | Async source extraction | 2026-07 | Property tests over the extraction state machine (task 8) were skipped |
| 6 | Review proposal workflow | 2026-07 | — |
| 7 | Ask the Loremaster | 2026-07 | — |
| 8 | Cost dashboard | 2026-07 | — |
| 9 | Worlds and campaigns restructure | 2026-07-09 | — |
| 10 | Artifact workflow (quotes, budget, quick-add, retrospective) | 2026-07 | Whether the retrospective was ever run on Symbaroum went unrecorded |
| 11 | Manual merge | 2026-07 | — |
| 12 | Processing visibility | 2026-07 | — |
| 13 | Browse and organization | 2026-07 | — |
| 14 | Graph view | 2026-07 | — |
| 16 | Storyline continuity (wrap-up, retrospective, health) | 2026-07 | Nav badge, auto-enqueue on staleness, per-world thresholds, durable seen-state |
| 17 | Knowledge reveal | 2026-07 | Un-reveal, library reveal, cross-visibility duplicate detection; per-character reveal indefinitely deferred |
| 18 | Journey map | 2026-07 (status paragraph is the authority; boxes are the plan) | Designated world map, relationship-expanded visits, per-character trails |
| 19 | Locations | 2026-08 | Per-place notes, pin editing from the view |
| 20 | Demo world and onboarding | 2026-08 | More than one template campaign, re-running a dismissed tutorial |
| 21 | Convergence gauge | 2026-08-06 | Automatic reveal on a threshold, per-character readiness |
| 22 | What you learned | 2026-08-06 | Notifications leaving the app, per-character views, GM audit |
| 23 | Character dossier | 2026-09-08 (all four phases) | Link back from an artifact's played-by names (declined); D3 is a standing instruction, not work |
| 24 | Interface and navigation — Nornis 2.0 | 2026-09-10 (all four phases) | Theme preference lives in the browser, not on `User`; the player-side live walk (V3) was not run |
| 25 | Players at the table | 2026-09-10 (all three phases) | The second-identity half of the live claim walk was not run; a member's player name is refreshed only at removal |
| 26 | Library excerpts | 2026-09-10 | Filing a whole document; re-filing after a re-upload; players filing from the party shelf |
| 27 | Shelf links | 2026-09-10 | The whole party-visible surface behind a link; expiry; links for members |
| 28 | The public face, brought up to date | 2026-09-10 | A per-world switch to withhold player names; a public character page; campaign filtering on the public Sources tab |
| 29 | Prep (Nornis 3.0) | 2026-11-02 (all five phases) | A persisted side conversation per Adventure; one-click PDF filing from a worker; the booklet reaching Bibliotaph; telling extraction which Adventure a session ran; printing only the props |
| 30 | Prep 3.1 (side conversation, threads to use) | 2026-11-02 (all three phases) | One-click PDF (deferred); telling extraction which Adventure a session ran; printing only the props |
| 31 | Bibliotaph (Nornis 3.2) | Shipped 2026-11-02 (Phases A to G; cutover H waits) | A "read in Bibliotaph" link for the creator; Nornis's own PDF index until the cutover (Phase H) |
| 32 | Adventure details (Nornis 3.4) | 2026-11-03 (Phases A to C; live walk C7 is Dave's) | Editing type, publisher or author (fixed by Dave); details for Library uploads that are not prints; using the details in drafting |
| 33 | Several sign-ins per account (Nornis 3.3) | 2026-11-02 | Merging two existing accounts (a one-off, by hand); showing when each sign-in was last used |
| 34 | Booklet as a PDF, one-click filing (Nornis 3.5) | 2026-11-03 (Phases A to C; the live walk is Dave's) | A4 page size; printing only the props; images in the booklet; widow and orphan control inside long paragraphs |
| 35 | Stat and item blocks in Prep (Nornis 3.6) | 2026-11-03 (Phases A to C; the live walk is Dave's) | Structured stat records with a form editor (declined); computing modifiers; importing stat blocks from Bibliotaph books |
| 36 | Library organisation (Nornis 3.7) | 2026-11-03 (Phase B; the live walk is Dave's) | Free-form tags and folders; linking one document to several Adventures; server-side paging |
| 37 | Prep design mode and source books (Nornis 3.8) | 2026-11-03 (Phases B and C; the live walk is Dave's) | Drafting all empty sections as a batch; live co-editing of the draft; page ranges within a source book |
| 38 | Cover art, rights page and handouts (Nornis 3.9) | 2026-11-03 (Phases B to D; the live walk is Dave's) | Pregenerated character cards; interior illustrations; a per-world image style |
| 39 | Bring your own AI keys, with a trial (Nornis 3.10) | 2026-11-04 (Phases A to C; the eval, the trial cutover and the live walk are still to do) | Keys per world; paying Nornis instead of bringing a key; tool use; embeddings after the cutover |
| 40 | Web research in Prep (Nornis 3.11) | 2026-11-06 (Phases A to C; the live walk is Dave's) | Search in Draft and Revise; search on the trial or OpenAI-compatible keys; fetching a page by URL; a Nornis-paid search vendor |
| 41 | Talk it through writes into the design draft (Nornis 3.12) | 2026-11-06 (Phases A and B; the migration and live walk are Dave's) | Streaming text live into the editor; Section mode writing on its own; a "suggest only" switch |
| 42 | World as hero: key art, the Home hero, type colours (Nornis 3.15, 3.16) | 2026-11-08 (Phase B as 3.15, Phases C and D as 3.16); the surface fixes from the same review shipped as 3.14 | An image per codex entry; an accent derived from the art; the Loremaster writing a key-art brief |

Some work was sequenced outside this folder: the August operations and test-quality backlog
lived in `docs/plans/` until 2026-09-10, when the finished plans were removed (they remain in
git history). Campaigns' first-tier page (2026-08-09) and the world digest were built from plan
items rather than numbered features.
