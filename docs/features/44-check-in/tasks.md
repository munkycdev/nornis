# Tasks

Ships as Nornis 3.42. One migration, `AddCheckIn` (three tables, one column on Worlds), which
Dave applies before the deploy.

**Status (2026-11-12): built in one PR; Phases A–D done, Phase E is Dave's.** Where the build
differed from the design: the question-count ceiling is `World.MaxCheckInQuestionCount` (10),
a constant beside the default, not a `CheckIn:MaxQuestionCount` option, so the validation in
`WorldService` and the clamp in `CheckInService` read one place; a storyline is "one-source"
only with exactly one source behind it (none means the GM made it by hand, the same rule as
entries), and the quiet signal still finds it once it has a development; the Home card answers
in place with the same `CheckInQuestionRow` the page uses, so there is one answering surface;
the settings card is its own `WorldSettingsSection.CheckIn` on the Processing tab.

**Specified 2026-11-12** in the thread "Reduce noise in the knowledge base", from Dave's
proposal of 2026-11-08 and his answers of 2026-11-12: both halves together; storylines, one-source
entries and texture patterns; every three dated sessions; the number of questions configurable;
and a larger list to work through when ambitious.

## Phase A — Domain and persistence

- [x] A0. `requirements.md`, `design.md`, this file; row 44 in the features README; amendment in
      `.kiro/steering/ai-extraction.md` (the brief in the prompt) and `domain-model.md` (the
      three entities).
- [x] A1. `CheckIn`, `CheckInQuestion`, `EditorialRule`; the three enums; `World.
      CheckInQuestionCount`; EF configurations; `ICheckInRepository`, `IEditorialRuleRepository`,
      `ISourceReferenceRepository.ListByWorldAsync`; migration `AddCheckIn`; in-memory fakes.
- [x] A2. `AiOperationType.CheckIn` placed in `AiTasks` and `AiSpendPools`;
      `ReviewBatchKinds.CheckIn`; `CheckInOptions`.

## Phase B — Candidates, the call, the answers

- [x] B1. `CheckInEvidence`, `CheckInCandidate`, `CheckInCandidates.Build` and its tests.
- [x] B2. `ICheckInAiClient`, `CheckInPrompt`, the Infrastructure client with schema and parse
      tests, the strict-dialect entry, the API test fake, DI in both branches of Program.cs.
- [x] B3. `CheckInService`: `GetAsync`, `StartAsync`, `AnswerAsync`, `AddRuleAsync`,
      `RemoveRuleAsync`, `CheckInCadence`, `CheckInAnswers`; service tests per requirement 5.
- [x] B4. `CheckInController` and its round-trip tests; `WorldsController` and `WorldService`
      carry the question count.

## Phase C — Extraction reads the brief

- [x] C1. `EditorialBriefPrompt.Render`; `ExtractionRequest.EditorialRules`; the section in
      `BuildSystemPrompt`; `ExtractionService` loads the rules; builder and service tests.

## Phase D — Web

- [x] D1. Client contracts and `NornisApiClient` methods; `CheckInCard` on Home; `CheckIn` page;
      the nav entry; the settings card; CSS.
- [x] D2. Change log 3.42, Features page line, version bump.

## Phase E — Dave

- [ ] E1. Apply the migration, deploy, confirm 3.42 on /health.
- [ ] E2. Ask me now on Symbaroum. The witch's question should arrive; answer Background on
      Koves and add the rule. Then extract the next session note and read the proposals.
