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

Work sequenced outside this folder — the August operations and test-quality backlog — lives in
[`../future-features.md`](../future-features.md) and `../plans/`. Campaigns' first-tier page
(2026-08-09) and the world digest were built from plan items rather than numbered features.
