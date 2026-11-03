# Design Document

> **Amended 2026-11-03 (Phase A as built).** The text below is the design as specified; where
> Phase A built it differently, this note wins.
>
> - **Funding is bound per call, not passed in.** Services don't call `AiFundingService` and
>   requests carry no `Funding`. Each request carries an `AiCallScope` (a world, with the acting
>   user, or a user alone for world naming) and `Model`, which now means "the model Nornis's own
>   key would use". `AiCallExecutor` asks `IAiCallBinder` (the design's `AiClientResolver`) on
>   every call; the binder asks `IAiFundingService.ResolveAsync(scope, task, nornisModel)` and
>   returns the client, the provider's traits and the `AiFunding`. Usage is priced on the
>   funded model. Phase A's `NornisAiFundingService` always answers "Nornis's Azure key, on
>   Nornis's model", so nothing changes in production.
> - **Refusals are exceptions, not `AppError`s.** Because funding is resolved inside the call,
>   Phase B refuses by throwing `AiKeyRequiredException`, `AiKeyRejectedException` and
>   `AiCreditExhaustedException`, each an `AiHttpException` (402 or 401). The existing catches
>   and `TransientFailureClassifier` already treat those as permanent failures; the Worker maps
>   them to the three new `ErrorCategories` and the API to the 409 codes below.
> - **`IAiProviderTraits` is smaller.** Clients describe a call with a provider-neutral
>   `AiCallOptions(AiJsonSchema? Schema, int? MaxOutputTokens)`; traits turn it into
>   `ChatOptions` (OpenAI: `ChatCompletionOptions` through `RawRepresentationFactory`, always
>   strict) and translate the SDK's exceptions. `ApplyCaching` and `ReadUsage` arrive with
>   Anthropic in Phase C.
> - **Extraction keeps `proposedValue`, closed.** Instead of `proposedFields`, `proposedValue`
>   is a closed object listing all 16 payload fields, each nullable and each required; the
>   prompt tells the model to send null for fields its changeType doesn't use, and the parser
>   drops the nulls, which rebuilds exactly today's payload. The model never has to escape a
>   value as JSON text. `StrictSchemaDialectTests` holds every schema to the dialect.
> - **Moved to Phase B:** the `AiModels` catalog (and replacing `ModelPricing`), the three
>   refusal exceptions and their `ErrorCategories`. There is no separate `Trial` model map: the
>   trial runs on the models Nornis's own configuration names.
> - **Phase B1 (2026-11-03): a fallback switch.** `AiModels:Fallback` says what pays when the
>   key holder has no key for a task: `Nornis` (Nornis's own key, unlimited, as before) or
>   `Trial` (the rule below). It ships as `Nornis` and becomes `Trial` at the cutover (Phase D),
>   so B and C can ship while Dave has no key without his worlds running out of trial. A saved
>   key is used in either mode.
> - **`ModelPricing` stays** for Nornis's own deployments (the trial and the `Nornis`
>   fallback); the catalog prices only key-funded models, and a key-funded call carries its
>   price on `AiFunding.Price` into the usage recorder. There are no `azure/…` catalog lines.
> - **The funding rule takes `needsImages`.** A call that sends images (detected from its
>   messages) skips models that can't read them: a choice without images falls back to the
>   default, and the default search skips them too. A user with only an OpenAI-compatible key
>   gets that key's line for the task as the default, which the rule as written left out.
> - **Phase B2 (2026-11-03), the API half.**
  - **A world's funding is its own route**, `GET /api/worlds/{id}/ai-funding` (GMs only), not a
    field on the world response: the Web loads the world list, and a funding lookup per world
    there would be a query per row. It answers `{ trial, trialRemainingUsd, creatorIsYou }`.
  - **Refusals answer 409** with their code (`ai_key_required`, `ai_key_rejected`,
    `ai_credit_exhausted`) from every API path: the non-streaming services catch
    `AiRefusalException` before their generic catch, the streaming ones (Ask, Prep drafting and
    conversation) classify it, and on-demand transcription maps the three categories. The
    exception's own 401/402 describes the provider's reply and would read to the browser as an
    expired session.
  - **A refusal is not a call.** `AiUsageRecorder` writes no row for one (the streaming paths
    always write their ledger row), matching the Worker, which records none.
  - **Saving without a key** re-checks the stored key: how a GM clears a failed check, or edits
    a compatible key's model lines, without pasting the key again. A line keeps its id while its
    task is listed, so a choice that names it survives the save.
  - **OpenAI-compatible endpoints** must be plain https (no credentials, query or fragment), and
    every connection to one, for the check and for calls, goes through `PublicEndpointGuard`:
    public addresses only, checked after DNS on each connection, no redirects. Without it a base
    URL could point the API or the Worker at the metadata service or a private address.
  - **Provider health is recorded by the binder** (`IAiCallBinder.ReportAsync`), not by the
    usage recorder: a failed call reaches the recorder without its usage, so without its
    provider. A parse failure no longer counts as the provider failing, since the provider
    answered. `/status` has `azure-openai` (Nornis's resource, which embeddings also report to),
    `ai-openai` and `ai-openai-compatible`; Anthropic and Gemini join with their clients.
  - **Requeue on save** runs in the save request, only when the user's keys now cover extraction
    (or the sources would fail again), in batches of 50 until none are left, through
    `SourceService.MarkReadyAsync` with the creator's role in each world. `Source.FailureCategory`
    is cleared by `NornisDbContext`'s save hook on any status but Failed, so no writer can leave a
    stale reason behind.
  - **The key-check limit** is ASP.NET's rate limiter, per user, 10 an hour, per API replica.
  - B2 ships as two PRs: the API (B5, B6b, B7) and the Web (B8).
- **`AiTask` lives in Domain** (`Nornis.Domain.Enums`), because `UserAiCompatibleModel` and
>   `UserAiModelChoice` store it; `AiTasks.For` stays in Application.
> - Fifteen clients were renamed (`AzureOpenAiExtractionClient` → `ExtractionAiClient` and so
>   on), not nineteen; `AzureOpenAiChatClient`, `AzureOpenAiClientFactory` and
>   `AzureOpenAiEmbeddingClient` stay Azure-specific. `AiProvider` has an `AzureOpenAi` value
>   for Nornis's own key.

## Overview

Two questions get answered before every AI call, in two places:

1. **Who pays, and with which model?** An Application rule, `AiFundingService`, reads the
   world's creator, their keys, their model choices and their trial balance, and returns an
   `AiFunding`: the catalog model, the provider, and either a key id or "trial". It returns
   an `AppError` instead when nothing can pay.
2. **Which client makes that call?** An Infrastructure resolver, `AiClientResolver`, turns an
   `AiFunding` into an `IChatClient` (decrypting the key, building and caching the client)
   plus the provider's traits: how it takes a JSON schema, how its errors map, how it caches.

The 19 AI clients keep their Application interfaces. They stop injecting one singleton
`IChatClient` built at startup with Dave's key. The executor binds the request's funding to a
client on every call.

```text
Service (Ask, extraction, Prep…)
  ├─ budget guard (unchanged)
  ├─ AiFundingService.ResolveAsync(worldId | userId, AiTask) ──► AiFunding | AppError
  └─ I…AiClient.Call(request { Funding, prompt… })
        └─ AiCallExecutor
              ├─ AiClientResolver.Bind(funding) ──► IChatClient + IAiProviderTraits
              ├─ traits.JsonSchema(schema) / traits.ApplyCaching(messages)
              ├─ client.GetResponseAsync / streaming (unchanged shell: timeout, usage)
              └─ traits.Translate(exception) ──► AiTimeout / AiParse / AiHttp
                                                 / AiKeyRejected / AiCreditExhausted
AiUsageRecorder ── price from AiModelCatalog, funding fields on the record
```

## Decisions

1. **`IChatClient` stays the seam.** OpenAI's .NET SDK reaches it through
   `Microsoft.Extensions.AI.OpenAI` (in use today). Anthropic's official C# SDK ships an
   `IChatClient` adapter. Gemini is reached either through Google's .NET GenAI SDK or through
   Gemini's OpenAI-compatible endpoint, whichever passes the conformance tests (task B0
   decides, and records which). Writing three HTTP clients of our own (option B in the
   discussion doc) or routing everything through one aggregator (option C) was declined.
2. **Keys belong to users, and a world spends its creator's.** The same rule as
   `BibliotaphConnection` ("a world uses its creator's"). Players never need a key. Per-world
   keys were declined: more setup, and the Worker's lookup would depend on world settings.
3. **Keys are Data Protection ciphertext in SQL.** A copy of `BibliotaphTokenProtector`: a
   ring of its own (`ApplicationName = "nornis-ai-keys"`) in a blob only the API and Worker
   identities can read, so the Web can't decrypt a key even with database access. There is no
   Key Vault (`azure-hosting.md`, 2026-09-09). Wrapping the ring with a Key Vault key later
   (`ProtectKeysWithAzureKeyVault`) would need no change to the column.
4. **The trial runs on the same models, capped.** A cheap trial model was Dave's first idea and
   was declined together on 2026-11-03: the Loremaster left gpt-5.4-mini in July for answer
   quality, and extraction is where a smaller model is worst. The trial is one-time, $2.00 per
   user, with a $25.00 monthly ceiling across all users. Its key is today's Azure OpenAI
   configuration, so the Azure resource stays, now as the trial's only.
5. **Trial balance is derived, not stored.** Trial spend is the sum of the user's
   `AiUsageRecord` rows funded by the trial. A stored balance would be a second copy of the
   ledger that can drift from it.
6. **Models come from a curated catalog.** The catalog lives in configuration
   (`observability-and-costs.md`: pricing in config) and replaces the three `ModelPricing`
   maps. A typo in a deployment name can no longer price a call at $0, because a model with no
   catalog entry or entered price is not callable. OpenAI-compatible models are the only
   models outside it, and carry the user's own prices.
7. **A failure for want of a key is a failure, not a new status.** Extraction already fails
   visibly with `ErrorCategories.BudgetExceeded` and lets the GM retry. Missing, rejected and
   exhausted keys follow it, with three new categories, instead of adding a "waiting" value to
   `SourceProcessingStatus`. Saving a working key queues those sources again (Requirement 5.5).
8. **One schema dialect for every provider.** Schemas are written in the strict dialect all
   four accept: every object closed (`additionalProperties: false`), every property required,
   optional values as a `null` union. The executor's OpenAI-only `RawRepresentationFactory`
   path goes, and each provider's traits translate the neutral schema. Extraction's open
   `proposedValue` object can't be closed, so it becomes a list of fields (below).
9. **Embeddings are left alone.** Only the upload index embeds, and Phase H of feature 31
   deletes it. Until then embeddings stay on Nornis's Azure key, unmetered against anyone's
   trial. They cost cents and would otherwise force Claude-only users to keep a second key.

## Data

```text
UserAiKey
- Id: Guid
- UserId: Guid → Users (cascade)
- Provider: AiProvider              // OpenAi | Anthropic | Gemini | OpenAiCompatible
- Label: string(100)                // the user's name for it; defaults to the provider's
- ProtectedKey: string(4000)        // AiKeyProtector ciphertext
- LastFour: string(4)
- BaseUrl: string(500)?             // OpenAiCompatible only
- AddedAt, UpdatedAt: DateTimeOffset
- CheckedAt: DateTimeOffset
- CheckFailed: bool                 // last check or last call rejected the key
- unique (UserId, Provider)

UserAiCompatibleModel               // OpenAiCompatible only: the user's own "catalog" lines
- Id: Guid
- UserAiKeyId: Guid → UserAiKey (cascade)
- Task: AiTask
- ModelName: string(200)            // the provider's model or deployment name
- SupportsImages: bool
- InputPerMillionUsd, OutputPerMillionUsd: decimal(10,4)   // 0 = free, never null
- unique (UserAiKeyId, Task)

UserAiModelChoice
- UserId: Guid → Users (cascade)
- Task: AiTask
- ModelId: string(100)              // catalog id, or "compatible:{UserAiCompatibleModel.Id}"
- key (UserId, Task)                // no row = the task's default

AiUsageRecord + Provider: AiProvider
              + ModelId: string(100)? // catalog id; null on rows before this feature
              + FundedBy: AiFundedBy?  // Key | Trial | Nornis (embeddings until Phase H)
              + AiKeyId: Guid?         // no FK: the record outlives the key
              + KeyHolderUserId: Guid? // index (KeyHolderUserId, FundedBy, CreatedAt)
```

`AiTask` is `Ask | Extraction | Prep | Chores`. Every `AiOperationType` maps to exactly one
task in `AiTasks.For(AiOperationType)`, an exhaustive switch, so a new operation can't compile
without a task:

| AiTask | Operations |
| --- | --- |
| Ask | AskLoremaster, ContinuityAudit, ContinuityFix, StorylineRetrospective, WorldDigest, CampaignRecap, HandwritingTranscription |
| Extraction | SourceExtraction, SourceExtractionRepair, ArtifactSummary, RelationshipBackfill, ImageReading, MapExtraction |
| Prep | AdventureDrafting, AdventureConversation |
| Chores | WorldNaming, ConvergenceNarration |
| (none: Nornis-funded) | Embedding |

Null, zero and empty: a missing `UserAiModelChoice` means "the default", never "off". A price
of 0 means free. `FundedBy` null appears only on rows written before this feature. A trial
allowance of 0 in configuration turns the trial off; there is no null for it.

### Model catalog (configuration)

```json
"AiModels": {
  "TrialAllowanceUsd": 2.00,
  "TrialMonthlyCeilingUsd": 25.00,
  "Trial": { "Ask": "azure/nornis-ask", "Extraction": "azure/nornis-extract",
             "Prep": "azure/nornis-ask", "Chores": "azure/nornis-ask" },
  "Catalog": [
    { "Id": "openai/gpt-5.4", "Provider": "OpenAi", "Model": "gpt-5.4",
      "Tasks": ["Ask", "Extraction", "Prep", "Chores"], "SupportsImages": true,
      "InputPerMillionUsd": 2.50, "CachedInputPerMillionUsd": 0.25, "OutputPerMillionUsd": 15.00 }
  ]
}
```

The `azure/…` entries are the trial's own catalog lines, priced as today, and are never
offered to users. Catalog order is the default order. The catalog's list of models is decided
in Phase C by the eval, not in this spec. Startup validation refuses a catalog entry with no
price, a task with no trial model, or a trial model that is not in the catalog.

## Funding rule

`AiFundingService.ResolveAsync(scope, AiTask, ct)` is the only place that decides who pays.
`scope` is a world (whose creator is the key holder) or, for a call with no world yet, a user.

1. The model is the key holder's choice for the task, or the task's default: the first catalog
   model, in catalog order, whose provider the holder has a key for.
2. If that model's provider has a key whose check has not failed, fund it with the key.
3. If the holder has a key for that provider but it failed its check, refuse with
   `ai_key_rejected` and the provider. The trial does not quietly take over from a broken key:
   the GM needs to know their key is broken.
4. If the holder has no usable key for the task, and the trial has a balance and this month's
   ceiling has room, fund it with the trial, on the task's trial model.
5. Otherwise refuse with `ai_key_required`.

On the public Ask path, step 4 is skipped (Requirement 4.5). The global AI pause and the
existing budget guard run first, unchanged; funding never weakens them.

Refusals are `AppError(409, code, message)`: `ai_key_required`, `ai_key_rejected` and
`ai_credit_exhausted` (the last one comes from a call, not from this rule). The Web renders
each code as its call to action. The Worker maps them to the three new `ErrorCategories`:
`AiKeyRequired`, `AiKeyRejected` and `AiCreditExhausted`. All three are non-transient.

## Infrastructure

- **`AiKeyProtector`**: Protect, Unprotect and `TryUnprotect` (a lost ring reads as a rejected
  key, as `BibliotaphTokenProtector` does). Configured by `AiKeys:KeyBlobUri`; ephemeral where
  nothing is configured (local development, tests).
- **`AiClientResolver.Bind(AiFunding)`** returns `(IChatClient, IAiProviderTraits, modelName)`.
  It caches built clients in a `MemoryCache` keyed by `(AiKeyId, UpdatedAt)`, with a size
  limit of 500 entries and a 10-minute sliding expiry, so a replaced key is never served from
  the cache. The trial's client is built once from today's Azure configuration. The SDK retry
  count stays the caller's (`RetriesOwnedByBackoff` in the Worker, `RetriesForUserFacingCall`
  in the API), as it is now.
- **`IAiProviderTraits`**, one per provider:
  - `ChatOptions JsonSchema(AiJsonSchema schema)`: OpenAI and Azure pass it through, strict.
    Anthropic sets its output format. Gemini removes the keywords it doesn't accept.
  - `void ApplyCaching(IList<ChatMessage>)`: does nothing for OpenAI (it caches a stable
    prefix by itself), and marks the end of the system prompt as a cache breakpoint for
    Anthropic. The extraction prompt is already built prefix-stable, so this is a hint
    rather than a redesign.
  - `Exception? Translate(Exception)`: maps the SDK's own exception types. A 401 or 403
    becomes `AiKeyRejectedException`. Out-of-credit becomes `AiCreditExhaustedException`
    (OpenAI's `insufficient_quota` 429, Anthropic's billing error, Gemini's quota-exhausted
    error). Any other 429 stays an `AiHttpException(429)`, which `TransientFailureClassifier`
    already retries.
  - `AiUsage ReadUsage(UsageDetails)`: maps cached-input and reasoning tokens wherever the
    provider reports them.
- **Key check** (`IAiKeyChecker`): lists models with the key, with a 10-second timeout. It is
  rate-limited per user to 10 checks an hour, so a stolen session can't use Nornis to test
  keys.
- **On a rejected call**, the executor sets `CheckFailed` on the key (one update), so the next
  call is refused by the funding rule instead of being sent.
- **Redaction**: the HTTP pipelines the AI SDKs use log no request headers. One test sends a
  call through a fake handler and asserts that neither `Authorization` nor `x-api-key` appears
  in any captured log or telemetry property. Watch it fail first, with logging of headers
  switched on.

## Extraction's schema

`proposedValue` is an open object today, deliberately, and a strict schema can only describe a
closed one. It becomes:

```json
"proposedFields": [ { "name": "string", "value": "string (JSON text of the value)" } ]
```

The extraction client's parser (today in `AzureOpenAiExtractionClient`) rebuilds from the
field list exactly the object it builds today, so `ReviewProposal`, review and apply don't change. A field whose `value` isn't valid
JSON is kept as a JSON string. The prompt's example and the steering doc's "Expected
extraction result shape" change with it. The map-extraction and relationship-backfill schemas
are checked against the same dialect.

A schema test walks every schema an AI client sends and fails on an open object or an optional
property. Watch it fail on today's extraction schema before it passes.

## Extraction eval (outside CI)

`tests/Nornis.Evals`: a console project, never run by CI (`testing-strategy.md`: no live AI in
normal CI). It runs extraction over a fixed corpus (a folder named on the command line, holding the five
Slime Spa chapters, which are never committed, and the demo world's session notes) on a chosen catalog model with a key from the environment. It writes a Markdown report
comparing the proposals with a reviewed baseline: proposals found, missed and invented,
schema failures, tokens and cost. The baseline is today's gpt-5.4 output, reviewed by Dave
once. A model enters the Extraction catalog when Dave accepts its report.

## API

```text
GET    /api/me/ai-keys                      list (provider, label, last four, dates, check state)
PUT    /api/me/ai-keys/{provider}           { key, label?, baseUrl?, models[]? } → check, then save
DELETE /api/me/ai-keys/{provider}
GET    /api/me/ai-models                    per task: the choices on offer, the current one, the default
PUT    /api/me/ai-models/{task}             { modelId }
GET    /api/me/ai-trial                     { allowanceUsd, spentUsd, remainingUsd, available }
```

All of them act on the signed-in user only; no other user's keys are reachable by any route.
A world's response gains `aiFunding: { trial: bool, trialRemainingUsd? }` for the GM, so the
Web can show "Trial: $1.40 left" without a second call. Players get nothing about money.

## Web

- **Profile → AI keys card**, beside Sign-ins: list, add or replace (a provider picker, then
  the key, then for OpenAI-compatible the base URL and one model line per task), remove (asks
  first). It carries the subscription note and links to platform.openai.com,
  console.anthropic.com and aistudio.google.com.
- **Profile → Models card**: one picker per task, from `GET /api/me/ai-models`, with
  OpenAI-compatible models marked "not tested by Nornis".
- **A trial line**, wherever a GM starts AI work (Sources, Ask, Prep, Costs): "Trial: $1.40
  left · Add your own key".
- **Refusals** render by code: `ai_key_required` and `ai_key_rejected` link to Profile;
  `ai_credit_exhausted` names the provider and links to its billing page.
- **Costs**: "Estimated" in every heading, a line saying the provider's bill is the real
  figure, and a "Paid by" column (a key's label, or "Trial").

## Worker

- Extraction and the other Worker AI paths call `AiFundingService` for the source's world
  before the AI call, next to the budget check. A refusal fails the source with the matching
  category, completes the message, and records nothing.
- Saving a key (API) finds the sources in worlds the user created whose last failure was one
  of the three key categories, and queues them again, 50 per batch, through the same path the GM's
  own retry of a failed source uses (task B6 names it). The set is bounded by the user's sources in those states.

## Health

`IAiOutcomeMonitor` records each outcome with its provider, and doesn't record key-required,
key-rejected or credit-exhausted outcomes at all: they say nothing about the provider. The
`/status` check becomes one entry per provider that has had traffic in the window ("ai-openai",
"ai-anthropic"…). The trial's Azure resource keeps its own entry.

## Testing

- `AiFundingService`: each branch of the funding rule, public Ask skipping the trial, the
  monthly ceiling, a balance going below zero mid-call (allowed) and the next call (refused).
- `AiTasks.For`: every `AiOperationType` value maps to a task (reflection over the enum, so a
  new value without a mapping fails).
- Traits: per provider, the schema translation (golden JSON), error translation from each
  SDK's real exception types, usage mapping.
- `AiKeyProtector`: round trip; a different ring can't read it; a lost ring gives null.
- API: keys never appear in any response body (assert on the serialized JSON); another user's
  key is unreachable; the check refuses a failing key and saves nothing.
- Worker: a refusal fails the source with the right category and isn't redelivered; saving a
  key queues exactly the key-failed sources of worlds the user created.
- Redaction and schema-dialect tests as above, each shown failing first.

## Rollout and downtime

One migration in Phase B (`AddAiKeys`: three tables, five columns on `AiUsageRecords`, one
index). Dave applies it by hand before merging, as usual. Existing usage rows keep null in the
new columns and still count for the budgets. There's no downtime beyond the usual revision
swap.

New configuration on `ca-nornis-api` and `ca-nornis-worker`, set with targeted
`az containerapp update --set-env-vars` (never `provision-azure.ps1` on the live stack):
`AiKeys__KeyBlobUri`, plus the `AiModels` section. The existing Azure OpenAI secrets stay:
they are now the trial's key.

At cutover Dave adds his own keys like any user. Until he does, his worlds run on the trial,
which a $2 allowance won't cover. So Phase D's first step is Dave saving a key, either a
direct OpenAI key or his Azure resource as an OpenAI-compatible key.
