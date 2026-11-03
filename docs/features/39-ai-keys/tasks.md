# Tasks

Four phases, one PR each. Built after the Prep wave (features 35 to 38) unless Dave moves it
earlier; feature 38's cover art is written against Phase A's resolver if A lands first, so it
doesn't need rework. The version number is set when Phase B ships. Each PR merges once its
checks pass (Dave, 2026-11-03: "you may merge PRs as you finish each one"). Phase B's migration is applied by Dave by hand before its PR merges.

**Status: specified 2026-11-03. Phase A built 2026-11-03; see the amendment at the top of
design.md for where it differs from the tasks below.**

## Phase A — The seam, with Azure still behind it (no behaviour change)

- [x] A1. Amendments: `ai-extraction.md` (the schema dialect, the closed `proposedValue`, the
      funding rule), `architecture.md` (binder and traits). `observability-and-costs.md` and
      `domain-model.md` move to Phase B with the catalog and the key entities.
- [x] A2. Application: `AiTask`, `AiTasks.For` (exhaustive), `AiFunding`, `AiCallScope`,
      `IAiFundingService`; `AiPromptRequest.Scope` (and on the three vision requests) as a
      `required` member. The refusal exceptions and `ErrorCategories` move to Phase B.
- [ ] A3. Moved to Phase B (B1a): the `AiModels` catalog and pricing from it.
- [x] A4. Infrastructure: `IAiProviderTraits` (OpenAI first), `IAiCallBinder` with Nornis's
      clients only (`NornisChatClients`, one per deployment), `AiCallExecutor` binding per call;
      15 clients drop the `AzureOpenAi` prefix and the singleton `IChatClient`; schemas and the
      drafting ceiling travel as `AiCallOptions`.
- [x] A5. `NornisAiFundingService`, which always funds Nornis's own model, so nothing changes
      for Dave.
- [x] A6. Schemas: the strict dialect everywhere; extraction's `proposedValue` closed with
      nullable fields (prompt, schema, parser). `StrictSchemaDialectTests` with its own
      rule test on the old open shape.
- [x] A7. Tests: `AiTasksTests`, `StrictSchemaDialectTests`, `OpenAiProviderTraitsTests`,
      `AiCallBinderTests`, executor binding tests, the null-dropping parser; build,
      `dotnet test`, `dotnet format --verify-no-changes`. Live check after deploy: one
      extraction on Gribbits still proposes the same kinds of change.

## Phase B — Keys and the trial (OpenAI and OpenAI-compatible)

Shipped as three PRs. **B1** (with migration `AddAiKeys`): B1a to B4 and B6a, behind
`AiModels:Fallback = Nornis`, so with no keys saved nothing changes. **B2 API**: B5, B6b, B7
and their tests, with no migration. **B2 Web**: B8. (2026-11-03: B2 was ready while B1 still
waited for its migration, so both went out in one PR, #97, merged once `AddAiKeys` is on prod.)

- [ ] B0. Decide Gemini's route (its .NET GenAI SDK or the OpenAI-compatible endpoint) with a
      conformance test of streaming, images, schema and usage; record the choice here.
- [x] B1a. (from A3) The `AiModels` catalog and its startup validation; `AiUsageRecorder`
      prices key-funded calls from it. `ModelPricing` stays for Nornis's own deployments (see
      the design amendment); there are no `azure/…` lines.
- [x] B1b. The refusal exceptions (`AiKeyRequiredException`, `AiKeyRejectedException`,
      `AiCreditExhaustedException`, subclasses of `AiHttpException`) and their three
      `ErrorCategories`.
- [x] B1. Domain and migration `AddAiKeys`: `UserAiKey`, `UserAiCompatibleModel`,
      `UserAiModelChoice`, the `AiUsageRecord` columns and index.
- [x] B2. `AiKeyProtector` with its own ring (`AiKeys:KeyBlobUri`). `IAiKeyChecker` and its
      rate limit move to B5, where saving a key needs them.
- [x] B3. `AiFundingService`: the real funding rule, the trial balance and ceiling, public
      Ask without the trial. Every AI entry point in the API and the Worker calls it.
- [x] B4. Binder: user keys for OpenAI and OpenAI-compatible; the client cache;
      `CheckFailed` on a rejected call. The redaction test moves to B5, with the key check.
- [x] B5. API: `/api/me/ai-keys`, `/api/me/ai-models`, `/api/me/ai-trial`;
      `/api/worlds/{id}/ai-funding` for GMs (design amendment: not a world field); refusal codes;
      `IAiKeyChecker` with its rate limit, the public-endpoint guard and the redaction test;
      `GET …/costs/by-funding` for B8's "Paid by".
- [x] B6a. Worker: refusals fail the source with their category, with no redelivery, and
      record nothing (extraction, maps, images, handwriting, summaries, backfill).
- [x] B6b. Saving a key queues key-failed sources again, through `SourceService.MarkReadyAsync`
      (the GM's own retry of a failed source), found by `Source.FailureCategory`.
- [x] B7. Health: outcomes per provider, recorded by the binder, refusals not recorded;
      `/status` entries `azure-openai`, `ai-openai`, `ai-openai-compatible`.
- [x] B8. Web: Profile's AI keys and Models cards, the trial line, refusal rendering, and
      "Estimated" plus "Paid by" on Costs. Refusals render through `AiErrorAlert` on Ask, the
      Prep drafting and conversation streams and Capture's page reading; an out-of-credit
      refusal links to the provider's billing page when its message names one.
- [x] B9. Tests per design.md; build, `dotnet test`, `dotnet format --verify-no-changes`.
- [ ] B10. Dave: the key-ring blob and `AiKeys__KeyBlobUri` on both apps (targeted
      `az containerapp update`), `AddAiKeys` applied, then the merge.

## Phase C — Anthropic, Gemini and the eval

- [ ] C1. `tests/Nornis.Evals`: the corpus runner and report; the gpt-5.4 baseline, reviewed
      by Dave once.
- [x] C2. Anthropic traits and client (official C# SDK, `IChatClient`), with explicit caching
      of the system prompt. Its tests (C5's half) are in `AnthropicProviderTraitsTests`.
- [x] C2b. The model picker: catalog models, then "More from your key" (design.md amendment).
- [ ] C3. Gemini traits and client (B0's route).
- [ ] C4. Eval runs on the candidate models of each provider; the catalog gets the ones Dave
      accepts, per task.
- [ ] C5. Tests per design.md for C2 and C3.

## Phase D — Cutover and ship

- [ ] D1. Dave saves his own key (a direct OpenAI key, or his Azure resource as an
      OpenAI-compatible key) and picks his models.
- [ ] D2. A live walk: extraction on Gribbits through Dave's key; a fresh test account on the
      trial reaches "Trial: $0.00 left" and the "Add a key" refusal; a bad key is refused on
      save; public Ask is off for a trial world.
- [ ] D3. Ship: `MinorVersion` bump; the change log, the Features page and the README; row 39.
