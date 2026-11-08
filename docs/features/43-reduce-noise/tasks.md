# Tasks

Ships as Nornis 3.21. No migration.

**Specified 2026-11-08** in the thread "Reduce noise in the knowledge base". Dave chose to drop
Incidental outright rather than file it as a hidden tier, and agreed the storyline test ("an
actor with intent and a conceivable resolution"). The check-in and editorial brief he proposed
are feature 44, not this one; the queue affordances (tiering, one-click reject, keeping the
original payload on edit) follow after.

## Phase A — Extraction judges significance

- [x] A0. `requirements.md`, `design.md` and this file; row 43 in the features README;
      amendment in `.kiro/steering/ai-extraction.md`.
- [x] A1. `ProposalSignificance`, `ExtractionProposal.Significance` (default Supporting), the
      schema field, the parser (absent is Supporting, unknown fails the parse). Client tests and
      the Anthropic fit test.
- [x] A2. `ExtractionTriage.Apply` and its tests; wired into `InvokeAiWithRetriesAsync` after
      validation; the eval runner applies it too. Service tests for the kept batch and the
      all-Incidental empty batch.
- [x] A3. The prompt: Significance section, Storylines section with the witch example, the Event
      line removed, open questions need pursuit, the imported-notes sentence. Builder tests.
- [x] A4. `ArtifactContext.Status`, set from the artifact and rendered as `Status:` for retired
      storylines; the Retired Storylines prompt section; reader and context tests.
- [x] A5. Change log 3.21, Features page line, version bump.

## Phase B — Measure (Dave)

- [ ] B1. Dave's next two or three Symbaroum session notes extract under the new prompt. Re-run
      `artifacts/kb-dump/Dump-WorldKb.ps1` and compare `per-source-review.csv` against the notes
      before it: proposals per note, Storyline creates per note, and whether a character
      reaction like the witch's warning arrives as a fact on the Character.
- [ ] B2. If a note still opens more than one Storyline on a regular basis, add the server-side
      cap in `ExtractionTriage` (design, "What this does not do").
- [ ] B3. When the provider keys are set, run the eval on old against new prompt over a handful
      of Symbaroum notes and record the counts here.
