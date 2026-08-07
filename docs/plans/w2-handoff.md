# W2 handoff — duplicate sweep, judgment done, execution remaining

2026-08-06. Fable made the calls and built the load-bearing halves on branch
`w2-duplicate-sweep`; this file is the spec for finishing it on Opus. Every judgment
call below is **made** — implement, don't re-litigate. When something here contradicts
the tree, the tree changed after this was written: stop and surface it rather than
forcing either one.

## What is already on the branch (WIP commit), and why — do not redo

1. **`ProposalApplicator.ApplyMergeArtifact` gained two guards** (self-merge; either
   side archived), placed after both artifacts resolve, before any mutation. Both merge
   paths — GM button and accepted proposal — funnel through this method, so the guards
   cover both. Cross-TYPE merges are **deliberately still allowed**; the comment in the
   code explains why (extraction mistypes artifacts; folding a mistyped twin into the
   right one is real cleanup). The AI's proposal rule is stricter than the system's
   permission rule — that split is intentional.
2. **`ContinuityFindingCategory.DuplicateArtifact`** added (string-persisted, 50-char
   column, fits — no migration).
3. **`ContinuityAuditService.SystemPrompt`** updated in BOTH places it lists categories
   (the "What to look for" bullets AND the "## Output" clause), plus a new
   "## Duplicates — a higher bar than the rest" section with concrete negative examples.
   The negative examples are the product judgment of this item; do not trim them.
4. **`ContinuityFixService`**: `DuplicateArtifact` findings branch to
   `DraftDuplicateMergeAsync` — **no AI call, no budget gate** (the pair comes from the
   finding's evidence; the survivor is computed). Direction rule, decided:
   **more facts+relationships wins; tie → older `CreatedAt`; tie → `Id`.** The rationale
   string states the direction, the reason, and flags a type mismatch for the reviewer.
5. **`tests/Nornis.Infrastructure.Tests/Ai/AzureOpenAiAuditClientTests.cs`**: schema↔enum
   drift tests. `GetStructuredOutputSchema`'s category enum must equal
   `Enum.GetNames<ContinuityFindingCategory>()` — strict structured output makes the
   schema the silent gate on what the model may report.

## Task 1 — the deliberately red test (do this first)

`AzureOpenAiAuditClient.GetStructuredOutputSchema()` (src/Nornis.Infrastructure/Ai/
AzureOpenAiAuditClient.cs, ~line 61) is currently MISSING `"DuplicateArtifact"` in the
`category` enum — an interrupted edit, left red on purpose as the handoff proof. Change:

```
"enum": ["Contradiction", "DanglingThread", "StaleStoryline", "TimelineConflict", "SummaryDrift"]
```
to
```
"enum": ["Contradiction", "DanglingThread", "StaleStoryline", "TimelineConflict", "SummaryDrift", "DuplicateArtifact"]
```

Proof: `SchemaCategories_MatchTheDomainEnum_Exactly` goes red→green. It has already been
demonstrated red with exactly this failure shape.

## Task 2 — tests (check what exists first)

A background agent was mid-flight writing tests when this handoff was cut. **Check
whether these already exist and pass before writing them**:

- `ProposalApplicatorTests`: self-merge fails `invalid_merge` AND relationships/status
  survive intact (the point: pre-guard, this path deleted every relationship then
  archived the artifact — a bare returns-an-error assertion would not catch the damage);
  merge into archived target fails and facts do NOT move; merge from archived source
  fails; cross-type merge SUCCEEDS (pins the deliberate permission).
- `ArtifactMergeServiceTests`: archived artifact rejected through the GM path (proves
  the applicator guard covers it — its own self-merge guard is already tested).
- `ContinuityAuditServiceTests`: a `DuplicateArtifact` finding citing two artifact refs
  round-trips with BOTH refs preserved in evidence.

Then write, in `tests/Nornis.Application.Tests/Services/ContinuityFixServiceTests.cs`
(match its fixture idiom — `Finding(...)` builder ~line 152, fakes in the fixture):

1. `DraftFix_Duplicate_DraftsTheMergeWithoutAnAiCall` — DuplicateArtifact finding whose
   evidence cites two live artifacts; assert: fake fix-AI client call count is ZERO,
   budget guard was not consulted (fake guard exposes call count or set Exceeded=true
   and assert it still succeeds — the stronger form; prefer that), one Pending batch of
   kind `ContinuityFix` with exactly one `MergeArtifact` proposal.
2. `DraftFix_Duplicate_KeepsTheRicherArtifact` — artifact A: 3 facts; artifact B: 1
   fact; assert proposal `TargetId == A.Id` and payload `sourceArtifactId == B.Id`.
3. `DraftFix_Duplicate_TieGoesToTheOlder` — equal weights, different `CreatedAt`;
   older is kept.
4. `DraftFix_Duplicate_EvidenceGoneWhenAPairMemberIsArchivedOrMissing` — one of the two
   archived → 409 `evidence_gone`, no batch minted.
5. `DraftFix_Duplicate_CrossTypePair_RationaleWarnsTheReviewer` — pair typed
   differently; assert the proposal `Rationale` contains "typed differently".
6. `BuildMergeRationale` is `internal static` — direction and tie wording can be pinned
   directly if the fixture route is awkward, but at least one test must go through
   `DraftFixAsync` end-to-end.

Also: `EnumDefinitionTests` has NO roster line for `ContinuityFindingCategory` — add one
(pattern at the `ReviewChangeType` pin, ~line 87-92) listing all six members. That
inherits the maintenance obligation deliberately: this enum now gates a destructive fix.

## Task 3 — docs records (paste, dated, house register)

`docs/plans/loremaster-wiki.md`, top of the `## W2` section, as a `>` block like W1/W3/W4:

> **Done 2026-08-06** (branch `w2-duplicate-sweep`; judgment by Fable, execution by
> Opus per the handoff spec in docs/plans/w2-handoff.md). The sixth category landed with
> three decisions the spec's one bullet didn't spell out:
>
> - **The fix path buys nothing from the model.** A duplicate's merge needs the pair
>   (the finding's own evidence) and a survivor (a counting question: more facts +
>   relationships wins, tie to the older entry — the create-dedup path's "the original
>   wins" — then Id). Asking a model for a fact we can count is spend without
>   information, and it adds the one error that matters here: direction reversed.
>   No AI call, no budget gate, rationale states the direction and why.
> - **The prompt's bar is asymmetric by design.** Every other category is advisory;
>   this one's fix archives an artifact. The prompt carries concrete negative examples
>   (shared surnames, place-vs-faction, parent-vs-child, cross-type pairs) and the
>   instruction that ambiguity means silence.
> - **The applicator grew the guards this feature makes urgent** — self-merge (which
>   deleted every relationship before archiving the artifact, reachable via an edited
>   proposal) and archived-either-side. Cross-type merges stay allowed at the system
>   level while the audit never proposes them: the AI's rule is stricter than the
>   permission, deliberately. A schema↔enum drift test now spans the seam no compiler
>   does — demonstrated red before green.
>
> Phase 2 (embedding candidate pairs) unbuilt, as the spec allowed — revisit if
> candidate quality disappoints. Known gap recorded, not fixed: draft-fix has no
> idempotency mark on the finding, so repeated clicks mint repeated Pending batches;
> pre-existing for all categories, now slightly more consequential. No migration.

`docs/future-features.md`, item 15: append **"Done 2026-08-06** on `w2-duplicate-sweep`
(Fable judgment, Opus execution — see the plan file's dated note for the three
decisions; the applicator's missing self-merge/archived guards were fixed as the
callee-owned defect this feature made urgent). No migration."

## Task 4 — the bar, then stop

`dotnet build Nornis.sln` (warnings are errors) → `dotnet test Nornis.sln` all green →
`dotnet format Nornis.sln --verify-no-changes` → clean worktree with explicit restore
(`git worktree add --detach <tmp> w2-duplicate-sweep`, restore, build, test, format).
Squash-or-keep: amend the WIP commit or add "W2: execution per handoff spec" on top —
either, but the final message must carry the three decisions (crib from the doc note).
**Do NOT merge or push**: main's deploy is blocked on a GitHub outage; the branch waits
for David's word like the others did.

## Out of scope — do not touch

- Phase 2 embedding candidates (spec says only if quality disappoints).
- Draft-fix idempotency (recorded above as a known gap; fixing it is its own decision).
- The writer-path summary-refresh edge from W1's note (W2's merges flow through the
  review queue, which does refresh — verified during recon; the edge is real only for
  the GM merge button and wrap-up/reveal, unchanged by this item).
- Web display mapping for the category — `DisplayText.Humanize` already renders
  "Duplicate Artifact"; a category icon would be the codebase's first and isn't wanted.
