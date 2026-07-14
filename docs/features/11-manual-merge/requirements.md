# Requirements Document

## Introduction

Bulk import leaves name-variant duplicates (Karvosti/Karvosthi) that only AI-proposed
`MergeArtifact` changes could previously fold together. GMs need a direct merge.

## Requirements

### Requirement 1: GM-initiated merge

1. A GM SHALL be able to merge a duplicate artifact into a target artifact in the same
   world; observers and players SHALL NOT (403).
2. Merging SHALL reuse the established MergeArtifact semantics: facts and relationships
   move to the target, self-referencing relationships are dropped, the duplicate is
   Archived, the target's UpdatedAt refreshes.
3. Merging an artifact into itself, across worlds, or involving a missing artifact SHALL
   fail with 400/404 without changes.

### Requirement 2: Provenance

1. THE merge SHALL be recorded as an accepted `MergeArtifact` review proposal in a batch
   tied to a synthetic GM-only source titled "Artifact merge — {duplicate} → {target} —
   {date}", so merges appear in the record like any other reviewed change.

### Requirement 3: UI

1. Artifact detail SHALL offer a GM-only "Merge into…" action: pick the target artifact
   (search by name), confirm with a plain-language description of what happens, then
   navigate to the target on success.
