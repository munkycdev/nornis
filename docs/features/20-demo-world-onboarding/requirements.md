# Requirements Document

## Introduction

Nornis currently onboards a new user with a blank page: sign in, create an empty world, and
the product's magic trick — messy session notes in, a living campaign memory out — is
invisible until they have run a real session through it. An *invited* user fares better
(their friend's world is already populated) but arrives as a player, seeing only the
consumption side of the app.

This feature closes that gap with three cooperating pieces:

1. **A demo world** — a per-user copy of a curated template campaign (*The Vespergale
   Reach*, content in [docs/demo-world](../../demo-world)), created from the
   [+ New World dialog](../../../src/Nornis.Web/Components/Shared/CreateWorldDialog.razor)
   or from a first-login prompt. The copy is a **snapshot**: five sessions of notes *plus
   their already-extracted knowledge* (codex, facts, relationships, map pins, GM-only
   secrets), so the user lands in a rich world instantly and at zero AI cost. One final
   session ("Session 6") is deliberately **not** in the snapshot — it exists as paste-ready
   text so the tutorial can run the real extraction pipeline exactly once.

2. **A tutorial** — a state-detected, resumable checklist in two chapters: *player basics*
   first (browse, journey, timeline, Ask), then *running a campaign* (paste Session 6, watch
   extraction, vet proposals, reveal a secret). The chapter split means an invited player can
   complete chapter one and stop with a finished, useful tutorial; the GM chapter waits
   below. The checklist detects completion from actual state changes, never from "did they
   click next," and can be **cancelled outright at any time** — dismissal is one action,
   permanent, and never re-prompts.

3. **A view-as-player toggle** — a GM control that makes the whole app render as a player
   sees it. This exists on its own merits ("what do my players actually see right now?") and
   is what makes tutorial chapter one honest: the demo user is GM of their copy, but chapter
   one runs in player view, and the transition to chapter two — flipping the toggle and
   watching hidden things appear — *is* the product pitch. The server already filters all
   reads by role ([VisibilityFilter.ForRole](../../../src/Nornis.Domain/Models/VisibilityFilter.cs);
   [PublicController](../../../src/Nornis.Api/Controllers/PublicController.cs) already forces
   `Observer` for anonymous reads), so this is a request-scoped role downgrade, not a
   filtering rewrite.

Product context that shapes the guardrails: Nornis is currently for invited friends, not a
mass-market product. Demo worlds may therefore publish public views like any world, but the
system carries a **kill switch** to disable that wholesale if it is ever abused. Demo world
creation triggers one real extraction (Session 6) per user who completes the tutorial, so
creation is lightly rate-limited.

## Requirements

### Requirement 1: Creating a demo world from the dialog

**User Story:** As a new or curious user, I want to create a ready-made demo world from the
same dialog I'd use to create a real one, so I can explore Nornis without inventing content.

1. THE + New World dialog SHALL keep "name → blank world" as its primary action, unchanged
   in flow, and SHALL add a visually secondary option: "Create the demo world".
2. The demo option SHALL include a checkbox "Walk me through it" (default **checked**) that
   enables the tutorial for the created world; unchecked, the demo world is created with no
   tutorial attached.
3. WHEN the demo option is chosen, THE system SHALL NOT require a name: the world's name is
   AI-generated (Requirement 2.4).
4. Demo creation SHALL be available to every user regardless of how many worlds they have,
   subject to the rate limit in Requirement 6.4.

### Requirement 2: Template instantiation

**User Story:** As the user creating a demo world, I want it born fully populated, so the
first thing I see is a living campaign rather than an empty shell.

1. THE system SHALL instantiate the demo world from a versioned **template package** — the
   world-export zip format ([WorldExportCategory](../../../src/Nornis.Domain/Enums/WorldExportCategory.cs))
   produced from a curated master world — rather than by re-running extraction.
2. The snapshot SHALL include: sessions 1–5 as processed sources, the map image attachment
   with its placemarks, all extracted artifacts/facts/relationships (including `GMOnly`
   elements), and the two GM prep sources; it SHALL NOT include Session 6 in any form.
3. THE creating user SHALL be the world's sole member, with role GM.
4. THE world's name SHALL be AI-generated per creation (so every demo world is named
   differently), with a static fallback list used when the AI call fails or exceeds a short
   timeout; name generation failure SHALL never fail world creation.
5. THE world SHALL be flagged as a demo world in the data model (Requirement 6 hangs
   behavior off this flag).
6. Instantiation SHALL be transactional: a failed copy leaves no partial world.

### Requirement 3: First-login onboarding prompt

**User Story:** As a user signing in for the first time, I want one clear suggestion of
where to start — and if I came via an invite, I want my friend's world front and center.

1. WHEN a user signs in and has never been shown the prompt, THE system SHALL show a
   dismissible onboarding prompt on the home page.
2. WHERE the user has at least one world membership (they arrived via an invite — invites
   land on home with the joined world selected, per
   [Invite.razor](../../../src/Nornis.Web/Components/Pages/Invite.razor)), the prompt SHALL
   lead with that world ("You've joined *X*") and offer the demo world as the secondary
   action; otherwise the demo world (with tutorial) SHALL be the primary action.
3. THE prompt SHALL be recorded as seen (per-user, server-side) the first time it is shown
   or dismissed, and SHALL never reappear; the + New World dialog remains the permanent
   path back to the demo option.
4. Dismissal SHALL be a single click and SHALL NOT trigger any follow-up nag anywhere.

### Requirement 4: The tutorial checklist

**User Story:** As a demo-world user, I want a visible checklist that walks me through the
product's key features and checks itself off as I actually do things — and I want to be able
to make it go away entirely if I don't want it.

1. THE tutorial SHALL render as a persistent checklist panel scoped to the demo world,
   showing two chapters: "Playing in a world" and "Running a campaign", with per-chapter
   progress (e.g. "3 of 5").
2. Each step SHALL deep-link to the page where it is performed and SHALL complete by
   **detecting the underlying state change** (e.g. "Add the Session 6 notes" completes when
   a new source exists; "Vet the extraction" completes when at least one proposal is
   decided), never by self-report; completion detection SHALL survive refresh and
   re-login (resumable).
3. Chapter one SHALL run in player view (Requirement 5) and SHALL be completable by
   someone who never opens chapter two; chapter two's first step is switching to GM view.
4. THE checklist SHALL offer a "dismiss tutorial" action that removes the tutorial
   permanently for that user with one confirmation at most; dismissal SHALL be recorded
   per-user and SHALL NOT re-prompt on any later visit, world creation, or login.
5. WHEN all steps of both chapters are complete, THE tutorial SHALL mark itself finished
   and collapse to an unobtrusive completed state (and be removable entirely per 4.4).
6. Steps SHALL tolerate being performed out of order and SHALL auto-complete if their state
   condition is already met.

### Requirement 5: View as player

**User Story:** As a GM, I want to see my world exactly as my players see it, so I can check
what is visible before and after reveals — and so the tutorial can teach the player
experience honestly.

1. THE nav's world card SHALL show the member's role as a quiet chip; for GMs the chip
   SHALL be the entry point to player view ("See this world as your players do").
2. WHILE player view is active, THE client SHALL render exactly what a `Player`-role member
   would receive: THE server SHALL honor an explicit view-as-player signal on read requests
   **only when the authenticated member's real role is GM**, downgrading the effective role
   for that request before any visibility filtering.
3. WHILE active, the state SHALL be unmissable: the world card visibly tinted, the chip
   replaced by "Viewing as player · back to GM", and GM-only chrome (Admin, World Memory,
   GM actions) absent.
4. Player view SHALL be ephemeral: client-side state, reset on reload, never persisted.
5. Write operations SHALL be unaffected by player view (the server continues to authorize
   writes against the real role); GM-only write affordances are simply hidden by 5.3.

### Requirement 6: Demo world guardrails

**User Story:** As the operator, I want demo worlds to be full-featured today but cheap to
rein in later, so an experiment among friends can't quietly become a liability.

1. Demo worlds SHALL support public views (`/w/{slug}`) exactly like real worlds, AND the
   system SHALL carry a server-side configuration kill switch which, when off, blocks
   enabling public access on demo worlds and stops serving existing public views of demo
   worlds; flipping it back restores them (mirroring the keep-the-slug semantics of
   [World.PublicSlug](../../../src/Nornis.Domain/Entities/World.cs)).
2. Demo worlds SHALL be excluded from any usage/health metrics that inform product
   decisions, identified by the Requirement 2.5 flag.
3. Public Ask on a demo world SHALL default to disabled (the existing
   `PublicAskMonthlyBudgetUsd = null` default suffices); a GM may enable it like any world.
4. Demo world creation SHALL be rate-limited per user (at most one demo world per user per
   day) — enough for honest re-runs, cheap insurance against extraction spam.
5. Deleting a demo world SHALL use the existing delete-world flow with no special casing,
   and SHALL NOT resurrect any onboarding prompt or tutorial (per-user flags survive).
