# Design Document

New Application service `ArtifactMergeService` (GM-gated):

1. Validate: GM role; both artifacts exist in the world; distinct ids.
2. Create a synthetic source (`GMNote`, `GMOnly`, `Processed`, title
   "Artifact merge — {duplicate.Name} → {target.Name} — {date}") and a ReviewBatch, in
   the same transactional pattern as the storyline retrospective.
3. Create a `MergeArtifact` ReviewProposal (TargetId = target,
   `{"sourceArtifactId": duplicate}`), then call `IProposalApplicator.ApplyAsync` and
   mark the proposal Accepted with the acting user — the merge IS the accepted proposal,
   so provenance and history need nothing new.

API: `POST /api/worlds/{worldId}/artifacts/{targetId}/merge` body
`{ "sourceArtifactId": <duplicate> }` → 200 with the refreshed target detail id.

UI: GM-only "Merge into…" button on the *duplicate's* artifact detail page; dialog with
a MudAutocomplete over the world's artifacts (excluding self), a warning describing the
move-and-archive semantics, confirm → call API → navigate to the target artifact.

Tests: service-level (role gate, cross-world, self-merge, happy path verifying facts
moved + duplicate archived + proposal recorded Accepted), plus the applicator behavior
already covered by ProposalApplicatorTests.
