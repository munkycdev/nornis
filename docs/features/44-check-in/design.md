# Design Document

## Overview

```
Home card / Check-in page
    GET  api/worlds/{id}/check-in                    ← due?, open check-in, standing instructions
    POST api/worlds/{id}/check-in                    ← Ask me now
    POST api/worlds/{id}/check-in/questions/{q}/answer
    POST api/worlds/{id}/check-in/rules, DELETE …/rules/{r}

CheckInService.StartAsync
    gather evidence        StorylineDevelopmentReader, artifacts, facts, references, sources
    CheckInCandidates.Build         ← pure: the bounded candidate list
    ICheckInAiClient.ChooseAsync    ← one call: picks ≤ N, phrases them
    persist CheckIn + CheckInQuestion rows (picked = Asked, the rest not)

CheckInService.AnswerAsync
    CheckInAnswers.AllowedFor(kind)         ← the one table of answers per kind
    action:  SyntheticBatchWriter.WriteAcceptedAsync (status)  |  IArtifactMergeService.MergeAsync
    rule:    IEditorialRuleRepository.CreateAsync (capped)

ExtractionService.BuildExtractionRequest
    EditorialRules = rules.Select(r => r.Text)
ExtractionPromptBuilder.BuildSystemPrompt
    EditorialBriefPrompt.Render(rules)      ← "## Standing Instructions", also in the check-in prompt
```

Three new tables, one new column, one migration. The feature composes what exists: the
continuity reader for the session axis and the quiet signal, the synthetic batch writer for
provenance, the merge service for folding a storyline into a character, the AI call seam for
funding and metering, and the extraction prompt builder for the brief.

## Domain

`CheckIn` (`CheckIns`): `Id`, `WorldId`, `CreatedAt`, `CreatedByUserId`, `Model`,
`LatestSessionSourceId?`, `LatestSessionAt?`, `ClosedAt?`. `LatestSessionAt` is the cadence
anchor: the next check-in is due once `CadenceSessions` dated sessions have a later date.

`CheckInQuestion` (`CheckInQuestions`): `Id`, `CheckInId`, `Order`, `Asked`, `Kind`
(`CheckInQuestionKind`: Storyline, Entry, Pattern), `SubjectArtifactId?`, `SubjectLabel`,
`Text`, `Why?`, `EvidenceJson` (the candidate's evidence as the AI saw it: related entries for the
record-on picker, predicates and counts for a pattern), `SuggestedRule?`,
`SuggestedRecordOnArtifactId?`, `Answer?` (`CheckInAnswer`), `AnsweredAt?`, `AnsweredByUserId?`,
`ActionBatchId?`, `RecordedOnArtifactId?`, `RuleId?`.

`EditorialRule` (`EditorialRules`): `Id`, `WorldId`, `Text` (≤ 300), `Origin`
(`EditorialRuleOrigin`: CheckIn, Manual), `CheckInQuestionId?`, `CreatedAt`, `CreatedByUserId`.

`World.CheckInQuestionCount` (int, default 5), updated through the existing partial world
update like `SummaryReviewRequired`; `WorldService` holds it to 1..`World.MaxCheckInQuestionCount`
(10), the one place the ceiling is stated.

Enums are stored as strings, as every enum here is. Cascade from `Worlds`; the question cascades
from its check-in. `EvidenceJson` is `nvarchar(max)`, written once and bounded by the candidate
caps.

The answers per kind live in one place, `CheckInAnswers.AllowedFor(kind)`, read by the service
to validate and by the Web through the response (`AllowedAnswers` on each question), so the
buttons on screen and the check on the server cannot disagree.

## Evidence and candidates

`CheckInEvidence` is the hand-buildable input: the storylines and their developments from
`StorylineDevelopmentReader` (GM visibility, so GM-only material counts), the continuity report
(for sessions-since-quiet), every non-archived artifact, every fact, every source reference in
the world, the dated sessions, the subjects answered in the two most recent check-ins, and the
world's rules. `CheckInCandidates.Build(evidence, options)` is a pure function returning
`IReadOnlyList<CheckInCandidate>` in rank order, each with a stable key (`c1`, `c2`, …), a kind,
a subject, a templated question and why, and the evidence block the prompt and the page render.

Rules, in the order they are applied:

- **Storyline**: Active, not answered recently, and either quiet (`SessionsSinceLastDevelopment
  >= StaleThresholdSessions` from the continuity report) or opened from one source (one distinct
  source across its references and its facts' references). Score = sessions quiet, plus 3 when
  one-source, plus open questions. Top 15. Related entries = the other endpoints of its
  relationships that are Characters or Factions, which is what the record-on picker offers.
- **Entry**: not a Storyline, not Archived, not answered recently, exactly one distinct source
  behind the artifact and all its facts and relationships, and that source is not one of the
  last `CadenceSessions` dated sessions (too new to judge). Order by fact count, then oldest
  created. Top 10.
- **Pattern**: facts grouped by predicate (trimmed, case-folded), excluding `open question`,
  `goal`, `development`, excluding predicates a rule already names in a Stop answer, with at
  least 8 facts across at least 3 artifacts. Order by fact count. Top 5. Evidence carries three
  example values and the artifact count.

Source references are read for the whole world in one query, `ISourceReferenceRepository.
ListByWorldAsync`, a new method, because "how many sources stand behind this entry" needs every
reference to the entry, its facts and its relationships. It is bounded by the world: Symbaroum is
about five thousand rows.

## The AI call

`ICheckInAiClient.ChooseAsync(AiPromptRequest) → CheckInAiResponse { Picks, Usage }` with
`CheckInPick { CandidateKey, Question, Why, SuggestedRule?, RecordOnArtifactId? }`. The
Infrastructure client sends a strict schema:

```json
{ "picks": [ { "candidate": "c1", "question": "…", "why": "…",
               "suggestedRule": "…" | null, "recordOnArtifactId": "…" | null } ] }
```

Two nullable fields, well inside the Anthropic adapter's limits. The schema joins
`StrictSchemaDialectTests`. The operation is `AiOperationType.CheckIn`, task Ask, pool DailyWorld;
both exhaustive switches fail their tests until it is placed, which is the point of them.

`CheckInPrompt.BuildSystemPrompt(questionCount)` says what the check-in is for, repeats feature
43's Storyline test (an actor with intent and a conceivable resolution) so the chooser applies the
extractor's definition, defines the three kinds and their answers, asks for at most N picks in
order of value (storylines that fail the test or have gone quiet first, then one-source entries
that look like texture, then patterns), one sentence per question naming the subject, one line
of *why* from the evidence given and nothing else, a suggested instruction only when the answer
would imply a general rule and phrased as an instruction to the extractor, and a record-on entry
only from the candidate's listed related entries. `BuildUserMessage(world, rules, candidates)`
renders the brief (so settled matters are not asked again) and the candidates with their keys.

The service validates every pick: unknown keys are dropped, duplicates collapse to the first,
more than N are truncated, a record-on id not among the candidate's related entries is nulled,
and a suggested rule over 300 characters is cut. The picked candidates become `Asked` questions
in the model's order with its text; the rest follow in rank order with the template text.

Fallback is honest rather than clever: a refused, failed or over-budget call starts no check-in
and returns the error, as the retrospective does. A world with no candidates skips the call.

## Answers

`AnswerAsync(AnswerCheckInQuestionCommand { WorldId, QuestionId, Answer, RuleText?,
RecordOnArtifactId?, ActingUser, Role })`:

1. GM only; the question must belong to an open check-in of this world and be unanswered
   (`409 already_answered`); the answer must be in `AllowedFor(kind)`.
2. The action, by kind and answer:
   - Storyline · Background with an entry: `IArtifactMergeService.MergeAsync(storyline → entry)`;
     the entry must be a non-archived artifact of the world (any type; the picker offers the
     related Characters and Factions first and the rest of the world after).
   - Storyline · Background without an entry: accepted `UpdateArtifact {"status":"Dormant"}`.
   - Storyline · Done: `{"status":"Resolved"}`.
   - Entry · Archive: `{"status":"Archived"}`.
   - everything else: no action.
   Status changes go through `SyntheticBatchWriter.WriteAcceptedAsync` with kind
   `ReviewBatchKinds.CheckIn`, a source titled "Check-in — {date}" whose body names the question
   and the answer. A subject already at the requested status skips the write (the wrap-up's
   retry rule).
3. The rule, if `RuleText` is non-empty after trimming: cap check (`409 brief_full`), then an
   `EditorialRule` with origin CheckIn and the question's id.
4. The question is stamped: answer, who, when, the batch, the entry, the rule. The action
   commits before the stamp, so a failure in the action leaves the question unanswered and the
   GM can try again; a failure writing the rule after the action succeeded is reported and the
   question is still stamped with the action it did, so a retry does not redo it.

A Pattern · Stop with an empty rule text still writes the templated instruction ("Do not record
{predicate}."), because Stop without a rule would do nothing at all.

## Cadence

`CheckInOptions` (section `CheckIn`): `CadenceSessions = 3`,
`MaxRules = 40`, `MaxStorylineCandidates = 15`, `MaxEntryCandidates = 10`,
`MaxPatternCandidates = 5`, `MinPatternFacts = 8`, `MinPatternArtifacts = 3`. Dated sessions are
processed sources with a session date that the GM can see. `CheckInCadence.SessionsSince(sessions,
anchor)` is the one counter, used for due-ness and for the entry rule's "too new" cut.

## Extraction

`ExtractionRequest.EditorialRules` (default empty). `ExtractionService` takes
`IEditorialRuleRepository`, lists the world's rules while assembling context and passes their
text through `BuildExtractionRequest`. `BuildSystemPrompt` inserts
`EditorialBriefPrompt.Render(rules)` between the introduction and What to Extract, so the model
reads the GM's instructions before the general guidance they override. Empty renders nothing.
The eval runner builds requests without rules and sees the prompt it saw before.

## Web

- `CheckInCard.razor` on Home, under the wrap-up, GM only. Due: a line and **Ask me now**. Open
  with unanswered asked questions: each as a row (question, why, the answer buttons; for
  Background a select of entries with the suggestion preselected; an "Also tell Nornis"
  field prefilled with the suggestion, shown when there is one or on request). Each answer
  saves as it is made and the row leaves the card. A footer link opens the page.
- `CheckIn.razor` at `/check-in`: Ask me now with the cost note and when the last one ran; the
  open check-in's asked questions, then **More to consider** (the unasked ones, same rows), then
  **Answered** collapsed with what each answer did; then **Standing instructions** with an add
  field and a Remove per line.
- `NavGroups`: "Check-in" in the GM group; the reachability test covers it.
- World settings, Processing tab: a **Check-in** card with Questions per check-in (1–10).
- `ReviewBatchKinds.CheckIn` is new; the queue never shows it (accepted batches are Completed).

## What this does not do, on purpose

- No background generation. Due is a state Home reads; the GM's click spends.
- No per-world cadence. The count is per world because Dave asked; cadence stays a server
  option beside the continuity thresholds until someone wants it otherwise.
- No deletion. Archive is reversible from the entry page; Stop leaves existing facts.
- The brief is not read by Ask, the retrospective or the audit. Extraction is where the noise
  starts.
- The brief and the check-ins are not part of the world export. A template world starts clean.
- No "consolidate the brief" pass. Forty lines of 300 characters is a page; if it fills, the
  GM prunes it, and a later feature can ask the Loremaster to merge lines.
