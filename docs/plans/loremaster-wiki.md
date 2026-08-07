# Loremaster wiki operations

> Part of the Nornis backlog. This file is a spec, not authorization: execute only
> through the Execution order in `docs/future-features.md`, which holds sequencing,
> completion status, and the Opus/Fable gate.

2026-08-01. Product of reading Karpathy's LLM-wiki pattern
(gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) against the tree. The
pattern: an LLM maintains a persistent synthesis layer between raw sources and
queries — three layers (sources → wiki → schema) and three operations (ingest,
query, lint). The reading's verdict: Nornis already *is* this pattern, with a
stricter trust model than the gist proposes (the review gate, TruthState,
visibility). What remains are four operations the pattern names that the tree
doesn't have. Ordered by value; W3 and W4 are independent of the rest.

**Already built — do not rebuild.** Recorded so the gist doesn't inspire
duplicates: the three layers are Sources → Artifacts → Views verbatim
(domain-model.md); the *lint* operation shipped as Continuity Health — heuristic
`IHealthService`, AI `ContinuityAuditService` (Contradiction, DanglingThread,
StaleStoryline, TimelineConflict, SummaryDrift; grounded evidence, dismissals),
and `ContinuityFixService` drafting fixes as ordinary Pending proposals; the
`log.md` analog is ReviewBatch history + SourceReference + AiUsageRecord. The
gist's "simple markdown indexing works at moderate scale" is the same bet
ai-extraction.md makes deferring vector retrieval — the pattern endorses the
architecture; these items only fill in missing operations.

Ground rules:

- Every new AI call goes through `IAiBudgetGuard` and the shared usage recorder
  (scrub **1.4**) and executor (scrub **1.5**) — no new hand-rolled tracking or
  catch ladders.
- Every synthesized batch gets a named `Kind`. The defect plan already flags the
  `Kind = null` divergence (D4, review-provenance item); nothing here may add to it.
- Visibility law is unchanged: nothing derived from GMOnly/Private material may
  surface at a wider scope (ai-extraction.md's default mapping).
- Migrations additive, as everywhere else in this file.

## W1 — accept-time summary maintenance

> **Done 2026-08-05** (branch `w1-summary-maintenance`). The policy decision this item was
> gated on is made and recorded where the spec asked — ai-extraction.md's 2026-08-05
> amendment: **trusted operation**, with provenance (an `ArtifactSummary` usage row +
> `Artifact.SummaryRefreshedAt`) and a per-world `SummaryReviewRequired` opt-back-into-review
> that files the fresh summary as a Pending proposal through the writer (batch kind
> `SummaryRefresh`). Beyond the spec's bullets, four decisions worth the record:
>
> - **The affected-artifact set comes from the applicator, not from payload re-parsing.**
>   `ApplyResult` now reports `SummaryRefreshCandidates` per arm — the one place that truth
>   exists (a merge stales its *target*; a PartOf move stales child, new parent, and the
>   parent it left; visibility/confidence-only changes report nothing) — plus
>   `SummaryPinnedArtifactIds`: an accepted proposal that *carries* a summary is the
>   reviewer choosing that text, and it cancels the refresh for that artifact across the
>   whole accept.
> - **Both accept paths trigger**, not just batch accept — a summary's freshness must not
>   depend on which button the GM clicked. Requests are coalesced per accept, and a
>   `RequestedAt` staleness gate against `SummaryRefreshedAt` collapses queued duplicates
>   into cheap skips instead of re-bought generations.
> - **The generation context is scoped to the artifact's own audience** (the ForSourceContext
>   gate, hidden-truths only for GM-only artifacts, and relationship lines dropped whole when
>   the far endpoint is invisible). The single Summary column forces this: the page is
>   rendered to everyone who can see the artifact, so the prompt is the leak surface — the
>   authorization tests assert on the captured prompt itself.
> - **Same queue, new kind**: `ExtractionKind.SummaryRefresh` rides the existing
>   source-extraction queue with an honest nullable `ArtifactId` on the message (the worker's
>   validation gate is kind-aware; smuggling the id through SourceId would have corrupted
>   every log line). The worker prices the call through ExtractionOptions — LoremasterOptions
>   is not configured worker-side, and an unknown model meters at $0.
>
> Known scope edge, recorded not fixed: accepted-shape writer operations (merge, wrap-up)
> apply through `SyntheticBatchWriter`, not `ReviewService`, so they do not yet trigger a
> refresh; the next review-queue accept touching those artifacts heals it. Migration
> `AddSummaryMaintenance` (additive: `Worlds.SummaryReviewRequired`,
> `Artifacts.SummaryRefreshedAt`) must be applied before the deploy that carries this.

The gist's core loop is "ingest updates the relevant wiki pages." Nornis ingests
into facts and relationships, but `Artifact.Summary` — the page — only changes
when a proposal happens to carry one. `AiOperationType.ArtifactSummary` is
declared and referenced nowhere: the MVP op ("generate artifact summaries from
accepted facts") was never built. The audit's SummaryDrift category exists to
*detect* the rot this operation would *prevent*.

- `IArtifactSummaryService`: artifact + accepted facts + relationships in, fresh
  summary out. Uses the dormant `ArtifactSummary` operation type.
- Trigger: after `BatchAcceptAsync` completes, enqueue a refresh for each artifact
  whose facts or relationships changed — Service Bus message, worker-side,
  budget-guarded; never inline in the accept request. Coalesce to one refresh per
  artifact per batch; skip pure-visibility changes.
- **Policy decision, made before implementation and recorded in
  ai-extraction.md:** does the refreshed summary route through review, or is it a
  "trusted system operation" under the core rule's own carve-out? Recommended:
  trusted operation — the summary is derived presentation over already-accepted
  knowledge, not new knowledge; forcing a review round per accepted batch would
  double review traffic to approve restatements. Record provenance either way,
  and let a per-world setting opt back into review if a GM wants the gate.
- Payoff: Ask grounds on current summaries (grounding order already puts
  artifacts first), SummaryDrift findings decay toward zero, and public Ask gets
  cheaper grounding — which stretches the monthly cap.

## W2 — whole-world duplicate sweep

> **Done 2026-08-06** (branch `w2-duplicate-sweep`; judgment by Fable, execution finished
> per the handoff spec in docs/plans/w2-handoff.md). The sixth category landed with three
> decisions the spec's one bullet didn't spell out:
>
> - **The fix path buys nothing from the model.** A duplicate's merge needs the pair (the
>   finding's own evidence) and a survivor (a counting question: more facts +
>   relationships wins, tie to the older entry — the create-dedup path's "the original
>   wins" — then Id). Asking a model for a fact we can count is spend without
>   information, and it adds the one error that matters here: direction reversed. No AI
>   call, no budget gate, and the rationale states the direction and why.
> - **The prompt's bar is asymmetric by design.** Every other category is advisory; this
>   one's fix archives an artifact. The prompt carries concrete negative examples (shared
>   surnames, place-vs-faction, parent-vs-child, cross-type pairs) and the instruction
>   that ambiguity means silence.
> - **The applicator grew the guards this feature makes urgent** — self-merge (which
>   deleted every relationship before archiving the artifact, reachable via an edited
>   proposal) and archived-either-side. Cross-type merges stay allowed at the system
>   level while the audit never proposes them: the AI's rule is stricter than the
>   permission, deliberately. A schema↔enum drift test now spans the seam no compiler
>   does — demonstrated red before green.
>
> Phase 2 (embedding candidate pairs) unbuilt, as the spec allowed — revisit if candidate
> quality disappoints. Known gap recorded, not fixed: draft-fix has no idempotency mark
> on the finding, so repeated clicks mint repeated Pending batches; pre-existing for all
> categories, now slightly more consequential. No migration.

Dedup runs only at ingest, against name-matched context — "Voss" and "Captain
Voss" created three sessions apart survive forever, and no audit category looks
for them. The machinery to *act* on duplicates exists end to end (`MergeArtifact`
change type, `ArtifactMergeService`); only the sweep that feeds it is missing.

- Cheapest first: a sixth audit category (`DuplicateArtifact`) in
  `ContinuityAuditService` — the prompt already reads the whole record; evidence
  is the two artifact refs. Extend `ContinuityFixService`'s allowed changes with
  MergeArtifact so the fix path can draft the merge.
- **Ordering dependency:** lands after the defect plan's merge fix (D2, the live
  duplicate↔target relationship row) — a sweep that triggers more merges before
  that fix multiplies the recurring DanglingThread it causes.
- Phase 2, if candidate quality disappoints: embeddings already exist
  (`AiOperationType.Embedding`, exact-KNN search verified sound) — compute
  name-trigram + embedding-similarity candidate pairs in SQL and have the LLM
  adjudicate only the candidates, instead of asking it to spot pairs unaided.

## W3 — the world digest (the gist's `index.md`)

> **Done 2026-08-05** (branch `w3-world-digest`), with the two-rendering bullet reversed on
> its own plan's ground rules:
>
> - **"Two renderings from one generation pass" became two scoped passes.** The visibility
>   law above says nothing derived from GMOnly/Private material may surface at a wider
>   scope — and a party recap produced by a pass whose context held GM material is derived
>   from it, whatever the output says. Asking the model to withhold is an instruction;
>   scoping the context is a guarantee (the same principle W1 shipped the day before, with
>   the same prompt-is-the-leak-surface tests). The party pass reads the **Observer-floor**
>   record — PartyVisible only, nobody's Private notes, Hidden truth states dropped,
>   relationships only between mutually visible endpoints, quotes only from visible
>   sources — because the recap renders to every member. Cost doubles on a GM-invoked,
>   infrequent call; that is the price of the guarantee.
> - **A party-empty world gets fixed text, not a third of a hallucination**: when the
>   Observer-floor record has no artifacts, the recap is a constant line and the second
>   pass is never bought — a generation over nothing could only invent.
> - Both passes share `ContinuityAuditService.FormatWorldRecord` and its caps, as the
>   grounding bullet asked — the two features cannot drift apart on what "the record" means.
> - One `WorldDigests` row per world (unique index, upsert, last-write-wins on the GM
>   double-click race), shown with its age. New `AiOperationType.WorldDigest`, one usage
>   row per pass. Surfaced as a Home rail card: GM sees digest + refresh + a "what players
>   see" preview; players see the recap.
> - **The auto-after-N-accepted-batches trigger is deferred**, GM-invoked only for now —
>   the eligibility/claim machinery the audit uses is the template when wanted.
> - Carries migration `AddWorldDigests` (additive, one CreateTable) — apply before deploy.

One maintained world-level synthesis: active storylines and their momentum,
recent movements, open questions. Storyline retrospectives and wrap-ups exist
per-storyline; nothing renders the state of the *world*.

- A generated read-model, **not** an artifact: an artifact's mutations must flow
  through review, and a derived page would pollute the knowledge graph it
  summarizes. In domain-model.md's terms a digest is a View. Persist the last
  digest per world with its generation time; show staleness rather than
  regenerating on every visit.
- Trigger: GM-invoked, plus optionally auto after N accepted batches. Grounding
  mirrors the audit's whole-record read (and shares its prompt-size guards).
- Two renderings from one generation pass: the GM digest (full, GMOnly) and a
  PartyVisible recap with hidden/GM material withheld — the second doubles as the
  session-recap and new-player-onboarding surface, which is the same need the
  demo-world/tutorial work keeps circling.

## W4 — Ask answers filed back

> **Done 2026-08-05** (branch `w4-ask-fileback`), on the extraction route David confirmed —
> and two of the bullets below reversed on contact with the tree:
>
> - **There is no `AskFileBack` batch Kind, and there must not be.** The filed answer is an
>   ordinary `GMNote` source created through the ordinary source API and marked ready, so the
>   batch it yields is the source's *own extraction batch* — the one kind whose null Kind the
>   filtered unique index and redelivery idempotency key on. A named Kind here would break
>   both. The "every synthesized batch gets a named Kind" ground rule is satisfied vacuously:
>   nothing synthesizes a batch, extraction earns one. The Retro/ContinuityFix pattern the
>   original bullet cited cannot apply anyway — those services hold structured drafts, while
>   an answer is free text, and the only thing that can turn free text into proposals *is*
>   extraction.
> - **The writer dependency dissolved with it.** The route reuses `GmNoteWriter`'s
>   create-then-mark-ready sequence (now shared between hand-written notes and filed
>   answers), so W4 touches no Application code at all — it is a Web-only change.
> - **"Visibility follows grounding" narrowed to "always GMOnly".** Nothing records which
>   scopes grounded an answer: retrieval filters by visibility and then discards it, and
>   citations carry no scope. GMOnly is the computable conservative end of the rule; Reveal
>   is the sanctioned promotion path, and the GM can widen individual proposals at review.
>   Widening this later means threading visibility through the knowledge-context models.
> - As predicted, smallest in the set: one shared `AskFileBackButton` on both member Ask
>   surfaces (page + rail panel), GM-gated and hidden in view-as-player, with a filed-marker
>   persisted into the localStorage history (nullable member — the store wipes history on
>   deserialization failure, so additions to those models must never be required).

The gist files good query answers back into the wiki; Ask is currently read-only.
When an answer synthesizes something not yet recorded — connects two facts, names
an implication — the synthesis evaporates when the conversation ends.

- A "file this" action on an Ask answer: the answer text becomes a synthesized
  GM-only source routed through ordinary extraction, yielding a reviewable batch.
  The provenance pattern already exists (StorylineRetrospectiveService,
  ContinuityFixService); new batch `Kind`, e.g. `AskFileBack`.
- **Ordering dependency:** waits for D4's shared synthetic-batch writer — built
  before it, this becomes the ninth hand-assembled copy of the provenance
  invariant.
- Visibility follows grounding: an answer grounded on GMOnly material files
  GMOnly. Smallest item in the set; UI is one button and a snackbar.
