# Requirements Document

## Introduction

Four workflow improvements driven by the first bulk import: surfacing source excerpts on
artifact detail, per-world AI budget configuration, a quick-add box that feeds new
information about an artifact through extraction, and an AI retrospective that proposes
closing out storylines the record shows are finished.

## Requirements

### Requirement 1: Source Excerpts on Artifact Detail

**User Story:** As a member reading an artifact, I want to see the quoted excerpt behind
each source reference and jump to the original source.

#### Acceptance Criteria

1. WHERE an artifact detail lists source references, THE UI SHALL display the stored
   quote text (when present) and the source's title as a link to the source detail page.
2. References without a quote SHALL still display the source link.
3. Visibility rules are unchanged: references already served by the API stay as-is.

### Requirement 2: Per-World AI Budget

**User Story:** As a GM, I want to set my world's daily AI budget from Settings instead
of asking an operator to change server configuration.

#### Acceptance Criteria

1. THE World entity SHALL carry an optional DailyAiBudgetUsd; when null, the server
   configuration default applies.
2. Only GMs SHALL set it; valid values are 0 (disable AI spend) to 100 USD.
3. THE budget guard SHALL enforce the world's value when set, else the configured default.
4. THE Costs page SHALL display the effective budget.

### Requirement 3: Artifact Quick-Add

**User Story:** As a member on an artifact page, I want to type what I know and have it
become reviewable knowledge about that artifact.

#### Acceptance Criteria

1. THE artifact detail page SHALL offer a text box that creates a source whose body is
   the entered text prefixed with "Regarding {artifact name}:", so the artifact is pulled
   into extraction context by name match.
2. THE source SHALL be created and marked ready in one action; proposals arrive in the
   normal review queue. Provenance is preserved (the note is an ordinary source).
3. Visibility SHALL be selectable (default PartyVisible); observers do not see the box.

### Requirement 4: Storyline Retrospective

**User Story:** As a GM after a bulk import, I want the AI to review my Active storylines
against the record and propose closing the ones that are clearly finished.

#### Acceptance Criteria

1. A GM-only action SHALL run a retrospective over Active storylines, producing
   UpdateArtifact status proposals (Resolved, Dormant, or Archived) with rationale, in a
   review batch — the user accepts or rejects each; nothing changes canon autonomously.
2. THE batch SHALL be tied to a synthetic source titled "Storyline Retrospective — {date}"
   recording what was assessed, so provenance survives.
3. THE pass SHALL be cost-tracked under its own AI operation type and gated by the daily
   AI budget.
4. Storylines the AI considers still active SHALL produce no proposal.
