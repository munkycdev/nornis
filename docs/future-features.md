# Future Features

Ideas discussed but deliberately not built. Each entry records the problem, the design
direction we'd start from, and why it's parked — so the thinking survives until the
problem actually bites.

Shipped and removed from this file: open questions on storylines (extraction convention +
storyline detail + factId-targeted resolution), AI-assessed continuity health, per-world
AI budget setting, artifact quick-add, source excerpts with links, storyline
retrospective, Auth0 login, logo/favicon.

## List of unprocessed features

* Find better icons for display
* Remove heuristic widget now that we have the AI-driven one
* Make the "Capture" page act more like Chronicis' so that I can reference external information while I'm taking notes
* Add a handwriting capture mode for iPad use
* Find some way to organize the Canon section
* I'd like to see the processing queue somewhere
* When does it make sense to make the Ask feature driven by Azure AI Search or some other RAG scheme?
* Retroactive excerpt capture: sources imported before per-proposal quotes (the whole Symbaroum bulk import) have quote-less references; a re-extraction or backfill pass could add them
* Link a member's Character record to its AI-extracted Character artifact (noted as future in domain-model.md)

---

## Graph view of artifacts

**Problem.** 360 artifacts and 345 relationships have no visual representation; the
connection structure of the world (who allies with whom, what lives where) is only
navigable one artifact at a time.

**Design direction.** JS graph library (Cytoscape or vis-network) via Blazor interop over
the existing artifacts + relationships endpoints. Filters by artifact type, status, and
campaign (via source provenance); click-through to artifact detail. Start with the
neighborhood view from a selected artifact — a full-world hairball of 360 nodes is a demo,
not a tool.

**Parked because:** requested 2026-07-14; queued behind the quick-wins batch and auth.
Largest UI lift on the list.

---

## Categorized tree projection of artifacts

**Problem.** The artifact list is flat; browsing wants grouping (by type → status →
name, or type → arc).

**Design direction.** Collapsible MudTreeView projection over the existing list endpoint —
pure client-side grouping, no API change. Cheap compared to the graph; likely the same
work item.

**Parked because:** requested 2026-07-14 alongside the graph view.

---

## Role-aware UI and auth polish

**Problem.** With Auth0 live (2026-07-14), every member sees the same UI: players see
GM-only controls (retrospective button, member management, GM visibility options) and get
server-side 403s instead of hidden affordances. Also: the Web access token lives 24h with
no refresh; when it expires mid-session the API starts returning 401s until re-login.

**Design direction.** The Web already knows the member's role per world
(`WorldSummary.MyRole`); hide GM-only affordances behind it (server enforcement already
exists — this is UX, not security). For tokens: request `offline_access` and refresh in
the bearer handler, or intercept 401s with a redirect to `/account/login`.

**Parked because:** single-user in practice until other players get accounts.

---

## Manual artifact merge

**Problem.** Name variants survive bulk import as separate artifacts (Karvosti/Karvosthi,
Farah Maroun/Moroun, "Ravens Expedition"/"The Ravens Expedition"). Merges currently only
happen via AI-proposed `MergeArtifact` proposals — there is no user-initiated path, so
known duplicates linger until an extraction happens to notice.

**Design direction.** `ProposalApplicator` already implements merge semantics; expose a
GM endpoint + UI (pick duplicate → pick target → merge) that reuses it, or a "propose
merges" AI pass over near-name-collisions like the storyline retrospective. Until then,
one-off merges can be done at the database level with the canonical spelling chosen by
the GM.

**Parked because:** a handful of known pairs, low churn; needs GM input on canonical
names either way.

---

## Content-filtered sources

**Problem.** Azure OpenAI's content filter rejects extraction for particularly grisly
session notes (HTTP 400 `content_filter`) — two Symbaroum sessions (2024-08-13 The Whip,
2025-04-03 Throne of Thorns) are permanently `Failed` with raw text preserved.

**Design direction.** Options in order of effort: manually soften the note text and
retry; apply for Microsoft's modified content-filter tier on the Azure OpenAI resource;
or catch `content_filter` specifically and mark the source with a distinct status/reason
so the UI can explain why it won't process.

**Parked because:** two sources out of 84; their raw text is safe in the record.

---

## Source authority & fact reliability weighting

**Problem.** Nothing distinguishes GM-ratified truth from player-attested truth. A Player
accepting an `AddFact` proposal from their own journal produces a fact indistinguishable
from one the GM confirmed. The GM should carry more canonical authority than a player,
even when the player accepts proposed facts from their own sources.

**Design direction (when needed) — in order of preference:**

1. **Role-clamped truth states** (cheap, uses existing vocabulary):
   - At extraction: pass the source author's world role into the prompt. Player-authored
     sources default claims to `Likely` at most (in-fiction assertions to `Rumor`);
     GM-authored sources keep `Confirmed`/`Hidden`. Enforce server-side the same way
     `EnforceVisibility` clamps visibility onto every proposal.
   - At acceptance: in `ProposalApplicator`, a non-GM acceptance cannot produce
     `Confirmed` (downgrade to `Likely`) or `Hidden`. GM acceptance passes through.
   - Requires the companion affordance: a one-click **GM promote** (Likely → Confirmed)
     so player testimony has a path to ratification. Scope these together or the clamp
     just feels punitive.
   - The Loremaster and Canon view need no changes — they already treat `Likely` and
     `Confirmed` differently.

2. **Numeric reliability weighting on accepted facts** (probably a trap):
   - A per-fact weight influenced by author role, source type, and acceptance authority.
   - Evidence against: `ArtifactFact.Confidence` already exists as a 0–1 numeric and is
     effectively ignored — no query sorts by it, the UI barely surfaces it, nobody misses
     it. Categorical truth states are what humans actually think in; a second number per
     fact is bookkeeping that will be ignored at the table.
   - Only revisit if role-clamping proves too coarse in practice (e.g. the table wants
     "trusted player chronicler" roles or per-source reliability).

**Spec note.** This is a spec addition, not just implementation — `domain-model.md` says
player-visible truth and GM truth must be separable, but is silent on authority-weighted
truth states. Update the steering docs if this gets built.

**Parked because:** no real world has hit the problem yet, and the honest risk is
building a rigor feature that gets ignored. Revisit when a table complains that player-
accepted facts polluted canon.

---

## Auto-accept for GM-authored sources

**Problem.** A GM making quick notes doesn't want to click through a review queue for
material they just wrote themselves.

**Design direction.** Ship "Accept all" per batch first (batch API exists and works) and
live with it. If friction remains, add an opt-in per-world setting: proposals derived
from GM-authored sources are auto-accepted on extraction. Risk: a bad extraction writes
wrong facts into canon with nobody looking — which is exactly what review exists to
prevent. Auto-accept should log loudly and be trivially reversible before it's trusted.

**Evidence from the field (2026-07):** the Symbaroum bulk import proved accept-as-you-go
is *required* for multi-source runs — extraction context only sees accepted knowledge, so
unreviewed proposals mean every source extracts against an empty world and duplicates
pile up. `scripts/import-notes.py` batch-accepts per source; the product feature would be
the same behavior as a per-world setting.

**Parked because:** the import script covers the bulk case; day-to-day single notes are a
few clicks. The spec's core principle is "AI proposes, users decide."

---

## Server-side Ask conversations

**Problem.** Ask history lives in browser localStorage — it dies with the browser and
can't sync across devices. The domain model already specs optional `Conversation` /
`ConversationMessage` entities.

**Design direction.** Persist conversations server-side per user per world; keep the
localStorage path as offline fallback. Enables future features: sharing an exchange with
the table, the Loremaster citing prior conversations.

**Parked because:** localStorage threading works. The old blocker (no auth to scope
history to a user) fell 2026-07-14 — revisit when multi-device use actually bites.
