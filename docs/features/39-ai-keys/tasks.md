# Tasks

Four phases, one PR each. Built after the Prep wave (features 35 to 38) unless Dave moves it
earlier; feature 38's cover art is written against Phase A's resolver if A lands first, so it
doesn't need rework. The version number is set when Phase B ships. Ask Dave before merging
these PRs. Phase B's migration is applied by Dave by hand before its PR merges.

**Status: specified 2026-11-03.**

## Phase A — The seam, with Azure still behind it (no behaviour change)

- [ ] A1. Amendments: `ai-extraction.md` (the schema dialect, `proposedFields`, the funding
      rule), `observability-and-costs.md` (`AiModels` catalog replaces `ModelPricing`; usage
      record funding fields), `architecture.md` (resolver and traits), `domain-model.md`
      (`UserAiKey`, `UserAiCompatibleModel`, `UserAiModelChoice`, AI task, key holder).
- [ ] A2. Application: `AiTask`, `AiTasks.For` (exhaustive), `AiFunding`, `AiJsonSchema`,
      `AiKeyRejectedException`, `AiCreditExhaustedException`, the three `ErrorCategories`;
      `AiPromptRequest.Funding` as a `required` member.
- [ ] A3. Configuration: the `AiModels` section and its startup validation; `AiUsageRecorder`
      prices from the catalog. The `azure/…` lines carry today's prices.
- [ ] A4. Infrastructure: `IAiProviderTraits` (OpenAI/Azure first), `AiClientResolver` with
      the trial client only, `AiCallExecutor` binding per call; the 19 clients drop the
      `AzureOpenAi` prefix and the singleton `IChatClient`; the `RawRepresentationFactory`
      schema path goes.
- [ ] A5. A temporary `AiFundingService` that always funds the trial models, so nothing
      changes for Dave.
- [ ] A6. Schemas: the strict dialect everywhere; `proposedFields` in extraction (prompt,
      schema, parser); the schema-dialect test, shown failing on today's schema first.
- [ ] A7. Tests per design.md for A2 to A6; build, `dotnet test`,
      `dotnet format --verify-no-changes`. A quick live check on Dave's Gribbits world that
      extraction still proposes the same things.

## Phase B — Keys and the trial (OpenAI and OpenAI-compatible)

- [ ] B0. Decide Gemini's route (its .NET GenAI SDK or the OpenAI-compatible endpoint) with a
      conformance test of streaming, images, schema and usage; record the choice here.
- [ ] B1. Domain and migration `AddAiKeys`: `UserAiKey`, `UserAiCompatibleModel`,
      `UserAiModelChoice`, the `AiUsageRecord` columns and index.
- [ ] B2. `AiKeyProtector` with its own ring (`AiKeys:KeyBlobUri`); `IAiKeyChecker` with its
      rate limit.
- [ ] B3. `AiFundingService`: the real funding rule, the trial balance and ceiling, public
      Ask without the trial. Every AI entry point in the API and the Worker calls it.
- [ ] B4. Resolver: user keys for OpenAI and OpenAI-compatible; the client cache;
      `CheckFailed` on a rejected call; the redaction test, shown failing first.
- [ ] B5. API: `/api/me/ai-keys`, `/api/me/ai-models`, `/api/me/ai-trial`; `aiFunding` on the
      world response for GMs; refusal codes.
- [ ] B6. Worker: refusals fail the source with their category, with no redelivery. Saving a
      key queues key-failed sources again; name the retry path it reuses.
- [ ] B7. Health: outcomes per provider, key outcomes not recorded; `/status` entries.
- [ ] B8. Web: Profile's AI keys and Models cards, the trial line, refusal rendering, and
      "Estimated" plus "Paid by" on Costs.
- [ ] B9. Tests per design.md; build, `dotnet test`, `dotnet format --verify-no-changes`.
- [ ] B10. Dave: the key-ring blob and `AiKeys__KeyBlobUri` on both apps (targeted
      `az containerapp update`), `AddAiKeys` applied, then the merge.

## Phase C — Anthropic, Gemini and the eval

- [ ] C1. `tests/Nornis.Evals`: the corpus runner and report; the gpt-5.4 baseline, reviewed
      by Dave once.
- [ ] C2. Anthropic traits and client (official C# SDK, `IChatClient`), with explicit caching
      of the system prompt.
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
