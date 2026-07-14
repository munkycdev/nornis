# Future Features

Ideas discussed but deliberately not built. Each entry records the problem, the design
direction we'd start from, and why it's parked — so the thinking survives until the
problem actually bites.

## List of unprocessed features

* Swap in logo and favicon images
* Find better icons for display
* Remove heuristic widget now that we have the AI-driven one
* Make the "Capture" page act more like Chronicis' so that I can reference external information while I'm taking notes
* Add a handwriting capture mode for iPad use
* Find some way to organize the Canon section
* I'd like to see the processing queue somewhere

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

**Design direction.** Ship "Accept all" per batch first (batch API already exists) and
live with it. If friction remains, add an opt-in per-world setting: proposals derived
from GM-authored sources are auto-accepted on extraction. Risk: a bad extraction writes
wrong facts into canon with nobody looking — which is exactly what review exists to
prevent. Auto-accept should log loudly and be trivially reversible before it's trusted.

**Parked because:** "Accept all" (in flight) likely covers 90% of the friction, and the
spec's core principle is "AI proposes, users decide."

---

## Server-side Ask conversations

**Problem.** Ask history lives in browser localStorage — it dies with the browser and
can't sync across devices. The domain model already specs optional `Conversation` /
`ConversationMessage` entities.

**Design direction.** Persist conversations server-side per user per world; keep the
localStorage path as offline fallback. Enables future features: sharing an exchange with
the table, the Loremaster citing prior conversations.

**Parked because:** localStorage threading works and auth (needed to scope server-side
history properly) is itself deferred.

---

## Open questions on storylines

**Problem.** The spec's storyline detail calls for "open questions," but no such concept
exists anywhere in the pipeline.

**Design direction.** Convention over schema: facts with predicate `open question` get
surfaced as their own section on storyline detail, and the extraction prompt is taught to
emit them for unresolved tensions ("What is the Silver Key for?"). Resolving one is an
`UpdateFact` proposal.

**Parked because:** needs the worker running end-to-end to be worth wiring.