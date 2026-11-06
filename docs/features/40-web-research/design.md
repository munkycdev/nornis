# Design Document

> **Amendment (2026-11-06, Phase B):** what building it changed. The text below is left as written.
>
> - **OpenAI on Responses**: `OpenAIClient.GetResponsesClient().AsIChatClient(model)`, which the
>   SDK marks for evaluation (`OPENAI001`, suppressed in `UserKeyChatClients.BuildResponses` and
>   the traits only). The body is `tools: [{"type":"web_search"}]`, `max_tool_calls`,
>   `max_output_tokens` and **`store: false`**: the Responses API keeps a response for 30 days
>   unless told not to, and a Prep prompt carries Hidden truth.
> - **OpenAI counts searches by call id**: the adapter reports a `web_search_call` as it starts
>   and again when done. Once usage arrives the count is complete, so a research turn that ran
>   none reports zero, not null.
> - **The binder** takes `needsWebSearch` and throws `InvalidOperationException` on funding
>   `WebResearchAvailability` wouldn't allow. A whole-response call (`AiCallExecutor.ExecuteAsync`)
>   refuses a search outright: only the stream reads the pages.
> - **The migration has four columns, not three**: `PrintDetails` is owned columns on
>   `LibraryDocuments`, not a JSON snapshot, so `PrintDetails_ResearchedOnWeb` (bit, null) joins
>   `ConversationMessages.WebResearchJson`, `Adventures.AiResearchedOnWeb` and
>   `AiUsageRecords.WebSearches`. `PrintDetails.Resolve` starts filling it in Phase C.
> - **The research prompt** also says never to put the world's record, above all Hidden truth,
>   into a search: the queries leave the provider for its search backend.
> - **The 409** is checked after the budget, before the GM's message is saved. A world with no key
>   and no trial reads as `NeedsOwnKey`. The page bounds stay in `WebResearchResult` (Domain), not
>   `PrepOptions`.
> - **Gemini's suggestions** render in `<iframe sandbox="allow-popups allow-popups-to-escape-sandbox" srcdoc>`
>   behind a `<base target="_blank">`, so a chip opens Google in a new tab; no `allow-scripts`, no
>   `allow-same-origin`.

> **Amendment (2026-11-06, Phase A):** what the spike found. The text below is left as written.
>
> - **No live calls yet.** The cloud container has no provider keys, so A1 probed the pinned SDKs
>   (Anthropic 13.3.0, Google.GenAI 1.28.0, Microsoft.Extensions.AI.OpenAI 10.11.0) against fake
>   providers fed streams shaped from each provider's documentation. `WebResearchWireTests` holds
>   those streams. `LiveWebResearchTests` (Phase C) checks them against the real providers.
> - **Claude**: `new ToolUnion(new WebSearchTool20250305 { MaxUses = n }).AsAITool()` sends
>   `{"name":"web_search","type":"web_search_20250305","max_uses":n}`. The adapter maps citations
>   to `CitationAnnotation` (url, title, cited text) and the search count to
>   `UsageDetails.AdditionalCounts["WebSearchRequests"]`, so no raw reading is needed. The basic
>   version stays: `web_search_20260209` and later default `allowed_callers` to code execution and
>   need `["direct"]` to avoid it. The adapter requires `web_fetch_requests` beside
>   `web_search_requests` in usage, as the real API sends it.
> - **Gemini**: `HostedWebSearchTool` sends `tools: [{ "googleSearch": {} }]`. The adapter reports
>   grounding as a `WebSearchToolCallContent` whose `Queries` are the searches and whose raw form
>   is `GroundingMetadata` (chunks with redirect URIs and site titles, and
>   `SearchEntryPoint.RenderedContent`). Pages come from the chunks; Gemini adds no citation
>   annotations to the text.
> - **OpenAI**: the SDK names are `OpenAIClient.GetResponsesClient` and
>   `CreateResponseOptions.MaxToolCallCount` (`max_tool_calls`), so OpenAI gets a hard cap too.
>   Until Phase B, asking OpenAI's traits for web search throws `NotSupportedException`.
> - **Prices** (providers' pricing pages, 2026-11-06): OpenAI $10 per 1,000 calls on reasoning
>   models, search content billed at model rates; Claude $10 per 1,000 searches plus tokens;
>   Gemini 3 and later $14 per 1,000 queries after 5,000 free a month (shared), with a 1,500 a day
>   cap. The catalog ignores Gemini's free allowance, so the Prep budget errs high.
> - **The bounds live in the Domain**: `WebResearchResult.Create` (pages deduped by URL, http and
>   https only, titles cut, host as a missing title, 20 pages, suggestions dropped whole over
>   32 KB). The collector in Infrastructure only gathers.
> - **Can't search** is `IAiProviderTraits.RefusedWebSearch`, checked by the executor only on a
>   call that asked to search, before `TranslateFailure`, and raised as
>   `AiWebSearchUnsupportedException` (an `AiHttpException` with status 400).
> - Gemini's terms on showing search suggestions weren't read (the terms page wasn't fetched);
>   Phase B shows them sandboxed as designed, which meets the documented ask either way.

## Overview

```
Web: Talk it through ── Web research switch ── POST prep/{id}/conversation/messages { webResearch: true }
                                                     │
AdventureConversationService.SendAsync ── WebResearchAvailability.For(funding)  ← the one rule
                                                     │ (refuses 409 web_research_unavailable before the stream)
AiPromptRequest.WebSearch = new(MaxSearches: 5, OutputCeiling: 6000)  (timeout 180 s)
                                                     │
AdventureDraftingAiClient → AiCallExecutor.StreamAsync(options with WebSearch)
                                                     │
AiCallBinder.BindAsync(…, needsWebSearch: true) ── UserKeyChatClients.For(key, model, secret, shape)
       OpenAI: Responses client (search calls only)   Claude / Gemini: today's client
                                                     │
IAiProviderTraits.ChatOptions(options)    → the provider's search tool, nothing else
IAiProviderTraits.ReadWebResearch(update) → web pages + search count, per provider
                                                     │
AiStreamChunk (last) { Usage.WebSearches, WebResearch { Pages, SuggestionsHtml } }
                                                     │
ConversationMessage.WebResearch (JSON, null = not a research turn)    AiUsageRecord.WebSearches
Put in section / draft / Apply of a researched answer → Adventure.AiUse.ResearchedOnWeb
PrintDetails.ResearchedOnWeb → RightsStatement.WebResearch line on page 2
```

## Decisions

- **Provider-native, not a Nornis tool.** Dave's choice (2026-11-06). No search vendor, no new
  secret, no tool loop: all three providers run the search server-side inside the one call, so
  the executor stays a single request and stream.
- **Only the side conversation searches.** Draft and Revise write into a section directly; a
  research answer passes through the GM's eyes before Put in section. Search there can come later.
- **Search only, never fetch.** The prompt holds Hidden truth. A tool that fetches a URL the
  model chooses can put a secret in the URL. Search queries go only to the GM's own provider,
  which already has the whole prompt.
- **OpenAI moves to the Responses API only for search calls.** Web search isn't offered on Chat
  Completions for the catalog's models. Every other call stays on `GetChatClient`, so the pinned
  wire (`OutputCeilingWireTests`, the streaming reader) is untouched; the Responses path gets its
  own wire test.
- **Off by default, every turn.** Searches cost money per call, and a GM who forgets a sticky
  switch pays for every turn after. Open to change in review: remembering it per adventure is
  a one-line change on the Web.
- **Fail visibly on a model that can't search** (feature 39's rule). No silent fallback.

## Data

```
ConversationMessage
  WebResearch: WebResearchResult?   (new, JSON column WebResearchJson, nvarchar(max) null)

WebResearchResult (Domain.Models, sealed record)
  Pages: IReadOnlyList<WebPage>     at most 20
  Searches: int?                    null = the provider didn't say
  SuggestionsHtml: string?          Gemini's search suggestions, at most 32 KB; null otherwise

WebPage (Domain.Models, sealed record): Title (≤ 300), Url (absolute http/https, ≤ 2,048)

AdventureAiUse
  ResearchedOnWeb: bool             (new, column AiResearchedOnWeb, default false)

PrintDetails
  ResearchedOnWeb: bool?            (new, inside the existing snapshot; null on prints before 3.11)

AiUsageRecord
  WebSearches: int?                 (new, null = not a research turn or not reported)
```

Null, zero and empty, named once:

- `ConversationMessage.WebResearch` null: the turn wasn't a research turn (every row before
  3.11). Non-null with no pages: it was, and cited nothing ("Searched the web; no pages cited").
- `Searches` and `AiUsageRecord.WebSearches` null: not reported; 0: searched nothing. A null
  count is priced as zero searches and the row is marked estimated, like a missing usage today.
- `PrintDetails.ResearchedOnWeb` null: a print from before the feature, read as false.
- `SupportsWebSearch` false: the model can't search. Its price is a separate field, never a
  sentinel for the capability (`WebSearchPerThousandUsd` is required when the flag is true, and
  must be greater than zero).

One migration, `AddWebResearch`: the three columns, no backfill.

### Model catalog

```json
{ "Id": "openai/gpt-5.4", …, "SupportsWebSearch": true, "WebSearchPerThousandUsd": 10.00 }
```

`AiCatalogModel` gains `SupportsWebSearch` and `WebSearchPerThousandUsd`. `AiModelPrice` gains
`WebSearchPerThousandUsd` and `Cost(…, webSearches)`, so the price of a search lives where the
price of a token does, and `AiUsageRecorder` passes `usage.WebSearches`. `EstimateFor(provider)`
takes the priciest search price too. Prices are pinned in Phase A from the providers' pricing
pages, not from the options doc's approximations.

## The availability rule

`WebResearchAvailability.For(AiFunding funding, AiModelsOptions models)` in Application is the
only place that decides, returning `Available` or one of `NeedsOwnKey`, `NotOnCompatible`,
`ModelCantSearch`:

1. Funded by Nornis or the trial: `NeedsOwnKey`.
2. Provider `OpenAiCompatible`: `NotOnCompatible`.
3. A catalog model (`funding.ModelId` found in the catalog) without `SupportsWebSearch`:
   `ModelCantSearch`.
4. Otherwise, including an untested model: `Available`.

Two callers, both through `IAiFundingService.ResolveAsync(scope, AiTask.Prep, …)`, which spends
nothing:

- `AdventureConversationService.GetAsync` puts it on `ConversationView`, so the panel renders the
  switch and its reason. A refusal from `ResolveAsync` (no key and no trial) reads as
  `NeedsOwnKey`.
- `SendAsync` checks it again when the command asks for web research and refuses with
  `AppError(409, "web_research_unavailable", <the reason's line>)` before the GM's message is
  saved, so a stale page can't send a search turn the key can't run.

`AiCallBinder` asserts the same (an `InvalidOperationException` if a search call binds to
anything `For` wouldn't allow), so a third caller that skips the service still can't search on
Nornis's money.

## Infrastructure

- **`AiCallOptions`** gains `AiWebSearch? WebSearch` (`MaxSearches`).
- **`AdventureDraftingAiClient`**: `request.WebSearch is { } ws ? new AiCallOptions(MaxOutputTokens:
  ws.OutputCeiling, WebSearch: new(ws.MaxSearches)) : CallOptions`. `AiPromptRequest.WebSearch`
  is null on every other caller, so nothing else can ask.
- **`UserKeyChatClients.For(key, model, secret, AiClientShape shape)`**: `Chat` as today;
  `Responses` builds `OpenAIClient.GetResponsesClient(model).AsIChatClient()` (exact SDK name
  pinned in A1) for an OpenAI key and is refused for any other provider. The cache key includes
  the shape.
- **Traits, `ChatOptions(options)`**:
  - OpenAI: `HostedWebSearchTool`, and `max_tool_calls` = `MaxSearches` through the raw options
    factory if the SDK exposes it (else the prompt's limit only).
  - Claude: the SDK's `WebSearchTool20250305 { MaxUses = MaxSearches }` through `AsAITool()`
    (the newer `web_search_20260209` only if A1 shows it needs no code-execution tool on the
    catalog's Claude models). `AnthropicPromptCaching` keeps its breakpoint; the tool changes the
    cached prefix, so a research turn misses the cache once.
  - Gemini: `HostedWebSearchTool` (Google Search grounding). No cap; the prompt carries it.
- **Traits, `ReadWebResearch(ChatResponseUpdate update, WebResearchCollector into)`** (new): the
  provider's way of reporting pages and searches. OpenAI: `CitationAnnotation`s on text and
  `web_search_call` items. Claude: citations on text blocks and
  `usage.server_tool_use.web_search_requests`, read from `RawRepresentation` where the MEAI
  adapter doesn't map them. Gemini: `groundingMetadata` chunks, `webSearchQueries` and
  `searchEntryPoint.renderedContent`. A1 records each provider's real stream as a fixture before
  the reader is written. The collector applies the bounds (20 pages, title and URL lengths, http
  and https only, dedupe by URL, 32 KB of suggestions).
- **`AiCallExecutor.StreamAsync`** passes `needsWebSearch` to the binder, calls `ReadWebResearch`
  on every update when the call searched, and puts the result on the last chunk
  (`AiStreamChunk.WebResearch`, `AiUsage.WebSearches`).
- **Fetch-tool guard**: `ChatOptions` for a research call holds exactly one tool, and
  `WebResearchWireTests` reads each provider's request body and fails on any `web_fetch`,
  `url_context` or function tool.
- **Can't search**: each provider's `TranslateFailure` maps a 400 on a research call whose error
  names the search tool to `AiWebSearchUnsupportedException`, which the service renders as
  `AppError(422, "web_research_unsupported", …)` (Requirement 5). It is an API-only path, so no
  `ErrorCategories` entry.

## Application

- **`AdventureConversationPrompt`**: a research turn's system prompt adds a short "When you can
  search the web" block (reference material, not instructions; the record wins; own words; at
  most five searches; no links in the text, the pages are listed for the GM). Turns without the
  switch keep today's prompt byte for byte, so their cache prefix doesn't move.
- **`SendConversationMessageCommand.WebResearch`** (bool). The service sets `request.WebSearch`,
  the research timeout, and saves `answer.WebResearch` from the last chunk.
- **`WebPageLinks.Strip(text, pages)`**: turns `[text](url)` into `text` where `url` is one of the
  pages, ignoring tracking query strings OpenAI appends. Applied once, before the answer is saved.
- **AI use**: `TryAcceptDraftAsync` (Put in section) and the draft change path (Put in draft,
  Apply) take the message's `WebResearch is not null` and set `AiUse.ResearchedOnWeb` in the same
  save that sets `WroteText`. `PrepBoundaryTests` gains the three paths.
- **`RightsStatement.WebResearch`**: the one sentence; `BookletModel` adds it when
  `PrintDetails.ResearchedOnWeb is true`.

## API

- `GET prep/{id}/conversation`: `ConversationResponse` gains `webResearch { available, reason }`;
  each message gains `webResearch { pages[{title,url}], searches, suggestionsHtml }` or null.
- `POST prep/{id}/conversation/messages`: `webResearch: bool` (default false).
- `GET prep/{id}`: `aiUse.researchedOnWeb`.
- No new route, so the Prep 403 sweep covers it unchanged.

## Web

- `AdventureConversationPanel`: a switch left of Send, disabled with its reason; turns itself off
  after a send. Under a research answer, **From the web** with the links (`target="_blank"`,
  `rel="noopener noreferrer nofollow"`); Gemini's suggestions in an `<iframe sandbox srcdoc>`
  with no `allow-scripts`.
- Book details: the "researched on the web" line under the AI-use box.
- Print page and party-visible filing: the one-time warning, remembered per adventure in the
  browser (a reminder, not a record).

## Testing

- Application: `WebResearchAvailabilityTests` (each reason, untested model allowed);
  service tests for the 409 before save, the saved pages, `Strip`, and the AI-use record on all
  three write paths; the recorder prices searches; catalog validation refuses a search model
  without a price.
- Infrastructure, per provider (feature 39's lesson: provider limits need per-provider tests):
  the request carries exactly the search tool with its cap; a turn without the switch sends no
  tool (pinned against today's body); the stream fixtures yield the right pages and counts; a
  400 naming the tool becomes `web_research_unsupported`.
- Web: bUnit for the switch states, the pages, the sandboxed suggestions and the warning.
- Booklet: PdfPig for the rights line, present and absent.
- Each new guard is seen failing first.
- Live, outside CI: `LiveWebResearchTests` on `$env:NORNIS_OPENAI_KEY` / `NORNIS_ANTHROPIC_KEY` /
  `NORNIS_GEMINI_KEY`, one search each, skipped without a key.

## Rollout

Phase B carries the migration; Dave applies `AddWebResearch` before it merges (memory's
migration procedure). The catalog's `SupportsWebSearch` lines ship with Phase A but nothing reads
them until Phase B's switch.
