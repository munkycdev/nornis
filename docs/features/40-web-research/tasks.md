# Tasks

Ships as Nornis 3.11. One migration (`AddWebResearch`, Phase B), which Dave applies before that
phase merges.

**Specified 2026-11-06. Dave chose provider-native search over a Nornis-side search tool or
leaving it out, and renumbered: hardening is deferred until the feature work is done, so this is
feature 40.**

**Status (2026-11-06): Phase A merged (PR #110). Phase B built; `AddWebResearch` waits for Dave
before it merges. A1 ran against the pinned SDKs with documented stream shapes, not live
providers (no keys in the cloud container); see the design's amendments.**

## Phase A — Spec and seam

- [x] A0. `requirements.md`, `design.md` and this file; row 40 in the features README.
- [x] A1. Spike, recorded in the design as an amendment: each provider's real request and stream
      for one search (OpenAI Responses on gpt-5.4, Claude Sonnet 5.5, Gemini 3.8 Flash), saved
      as test fixtures with keys and ids scrubbed; the SDK names for the Responses client and
      `max_tool_calls`; which Claude search tool version runs without code execution; the
      current search prices; Gemini's current rule on showing search suggestions.
- [x] A2. `AiCallOptions.WebSearch`, `AiPromptRequest.WebSearch`, the catalog fields and their
      validation, `AiModelPrice` with searches, `AiUsage.WebSearches`, `WebResearchAvailability`.
      Tests.
- [x] A3. Claude and Gemini traits: the tool, `ReadWebResearch` over the fixtures, the
      can't-search translation, the fetch-tool guard. Wire tests.

## Phase B — OpenAI, the switch and the pages

- [x] B1. `AiClientShape.Responses` in `UserKeyChatClients`, the binder's `needsWebSearch` and its
      assertion, OpenAI traits for the Responses path. Wire tests, and today's Chat Completions
      body pinned unchanged.
- [x] B2. Domain and migration: `ConversationMessage.WebResearch`, `AdventureAiUse.ResearchedOnWeb`,
      `AiUsageRecord.WebSearches`, `PrintDetails.ResearchedOnWeb`. Dave applies `AddWebResearch`.
- [x] B3. Service and API: the command flag, the 409 before save, the research prompt block,
      timeout and ceiling, `WebPageLinks.Strip`, saving the pages, the availability on GET.
- [x] B4. Web: the switch, From the web, the sandboxed suggestions. bUnit tests.

## Phase C — Rights and release

- [ ] C1. The AI-use record on Put in section, Put in draft and Apply; `PrepBoundaryTests`.
- [ ] C2. `RightsStatement.WebResearch` on page 2; Book details line; the print and filing warning.
      PdfPig and bUnit tests.
- [ ] C3. `security-and-permissions.md` amendment (web content reaches a model holding Hidden
      truth; search only, never fetch; Prep only). Change log, Features page, README, version 3.11.
- [ ] C4. Live walk (Dave): the Waterdeep question on his Prep model; pages shown; searches on
      Admin, Costs; the rights line on a print.
