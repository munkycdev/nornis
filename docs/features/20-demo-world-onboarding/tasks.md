# Tasks

**Status: shipped July 2026.** Written 2026-08-06 from the tree — this feature had no tasks
file, so what follows records what is in it rather than what was planned.

## What is built

- [x] **Demo world** — `DemoWorldService` / `IDemoWorldService`, `DemoWorldOptions`,
  `CreateDemoWorldCommand`, and the `CreateDemoWorldRequest` contract. The template snapshot
  lives at `src/Nornis.Api/DemoTemplate`; its authored content is in
  [`docs/demo-world`](../../demo-world) — *The Vespergale Reach*, five sessions with their
  extracted knowledge, plus `session-06.md` left as raw text, and `make_map.py` for the map.
- [x] **Entry points** — the `+ New World` dialog (`CreateWorldDialog.razor`) and a first-login
  prompt (`OnboardingPrompt.razor`).
- [x] **Tutorial** — `TutorialService` / `ITutorialService`, `TutorialController`, and
  `TutorialChecklist.razor`. Two chapters, completion detected from state rather than from a
  next button, dismissable permanently.
- [x] **View as player** — a world-menu item in `NavMenu.razor` with an active chip to leave it;
  state on `WorldState`, honoured server-side via `HttpContextExtensions`.
- [x] **Tests** — `DemoWorldTests` and `TutorialTests` (`Nornis.Api.Tests/Controllers`).

## Not built

- [ ] More than one template campaign.
- [ ] Re-running the tutorial once dismissed.
