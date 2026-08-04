# Pre-Implementation Checks

Five questions to answer *before* writing a feature, and one rule for reading a bug report.

They are not style advice. Each one is derived from a defect this codebase actually shipped,
and each names the specific thing that would have caught it. Written 2026-08-03 after a week
in which most fixes were structural rather than logical — the code was locally correct
everywhere and wrong from above.

## Why these five

Sorting that week's fixes by root cause left four buckets and almost nothing else:

| Root cause | What it looked like here |
| ---------- | ------------------------ |
| A rule living in two places | The Web recomputing the continuity score it renders. The review-provenance stamp assembled by hand at eight sites. `MaxComposedBodyChars` duplicating `SourceService`'s literal. `JsonOptions` declared byte-identically in four files. Ask history persisted two different ways. World selection persisted in `NavMenu`, so three of four callers lost it. Twenty-seven raw `== "GM"` comparisons in three spellings. |
| A sentinel meaning two things | Zero meaning "no ceiling" in the daily budget and "switched off" in the public-Ask cap. Nullable DI dependencies meaning both "not configured" and "feature deliberately off". |
| Something unbounded | Ask history in localStorage. Abandoned `PendingUpload` rows. The indexing pipeline holding a whole document in RAM. The one AI call with no timeout. |
| A guard that could not fire | Reflection-only "403 tests" asserting an attribute exists. Constructor tests asserting `Is.Not.Null`. The format gate that ran on pull requests but not on push. |

Every one was written correctly for the situation in front of its author. None of them could
be seen without standing above the piece — which is exactly what these questions do.

## The five questions

### 1. Where does this rule live? Name the one place.

Before implementing a rule — a penalty table, a cap, a role test, a serializer configuration,
a status transition — say where it lives and make every other site call it.

If the answer is "in the razor and also in the service", stop. The razor's copy of the
continuity scoring survived because no compiler and no test spans a deploy boundary, and it
was found by reading, not by failing.

The corollary is the one comment rule worth restating here: *"mirrors X" is legitimate exactly
when no compiler can enforce the sameness.* If a compiler **can** enforce it, make it, and the
comment becomes unnecessary rather than deleted.

### 2. What happens when there is a second caller?

Behaviour placed at a call site is behaviour the next caller will not get.

`NavMenu.SelectWorldAsync` persisted the selection; `WorldState.Select` did not. Every caller
that was not the nav menu — accepting an invite, creating a first world, finishing onboarding
— selected a world for the session and handed the old one back on the next load. Three bugs,
one cause, and the cause was a correct decision for the only caller that existed at the time.

Ask it even when there is only one caller. Especially then.

### 3. What do null, zero and empty each mean here?

Name all three before shipping a nullable or a numeric threshold.

`DailyWorldBudgetUsd <= 0` disabled the spend guard. `PublicAskMonthlyBudgetUsd <= 0` blocked
the feature. Same literal, same file, opposite outcomes — and only a validator's `0.01` floor
standing between that and a spend guard that fails open.

A value that means "unset" must not also be a value that means "zero of the thing".

### 4. What is unbounded?

Every list that grows, every buffer that fills, every external call that waits.

Ask history had no cap and swallowed its quota exception, so a heavy user silently stopped
keeping history. The indexing pipeline held every embedding for a document at once — ~6 KB per
vector, with a file at the upload cap as the input. Embeddings were the one AI path without an
application timeout, so a hung call fell back to the SDK's own worst case, inside a
user-facing request and against a worker lock.

The answer may legitimately be "bounded by X elsewhere". Say which X.

### 5. Show it failing before showing it passing.

A guard that has never been red is a claim, not a guard.

Seven tests in `CostsControllerTests` asserted that an attribute existed. Four in
`ServiceBusExtractionProcessorTests` asserted a constructor returned an object. Both suites
were green and neither could fail for the reason its name gave. The format gate ran only on
pull requests, so two dependency bumps turned `main` red with nothing noticing.

For a new gate: break the thing on purpose, watch it fail, then fix it. For a new test:
invert one assertion and confirm the failure names the right thing.

This applies to verification of finished work too, and the failure mode is not hypothetical —
a MudBlazor upgrade was once "verified" against a working tree that still had the old version
loaded. Verify against a clean checkout with an explicit restore, never the tree you have been
editing.

## And one rule for reading a bug report

**When a defect is described at a call site, ask whether it belongs to the callee.**

A report says where somebody *noticed* the problem, which is rarely where it lives. This
earned itself three times in one week:

- "Accepting an invite doesn't persist the world selection" — three callers were broken.
- "CostsControllerTests' region promising a 403 test" — there were three such regions.
- "Nullable optional DI dependencies in `ExtractionService` and `ReviewService`" — the fix
  belonged to the constructor contract, not to the two services named.

Fixing the sighting leaves the defect. Widening the scope is usually right; when it is, say so
in the commit rather than quietly doing more than was asked.

## What this file does not cover

Process, not prompting: the two habits that catch what these questions do not are verifying
in a clean worktree with an explicit restore, and recording *why* — including where reality
contradicted a plan. See how `azure-hosting.md` and `coding-standards.md` carry dated
amendments rather than silent edits. Two plan documents were caught making stale claims in a
single day — a "keep untouched" list naming a mirror that had since been removed, and a bullet
still reading as open six days after its fix shipped — and both were catchable only because
the surrounding entries record what happened rather than what was intended.
