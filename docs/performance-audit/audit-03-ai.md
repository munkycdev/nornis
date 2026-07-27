# Nornis AI / LLM Cost & Performance Audit

**Scope:** everything AI/LLM — `src/Nornis.Application/Ai/`, `src/Nornis.Infrastructure/Ai/`, prompt text, callers, and the two workers insofar as they drive AI calls.
**Method:** every finding below was confirmed by reading the source. Anything I could not confirm is in "Unverified / worth a look".

## Provider and models in use

**Azure OpenAI only.** `Azure.AI.OpenAI` 2.1.0 (`src/Nornis.Infrastructure/Nornis.Infrastructure.csproj:8`, `src/Nornis.Worker/Nornis.Worker.csproj:11`). No Anthropic/Claude, no OpenAI direct, no Gemini — so the `claude-api` skill does not apply and was deliberately not loaded.

| Deployment | Model | Host | Rate (per 1M) | Drives |
|---|---|---|---|---|
| `nornis-extract` | gpt-5.4 | Worker | $2.50 in / $15.00 out | source extraction, handwriting transcription, image reading, map extraction, relationship backfill |
| `nornis-ask` | gpt-5.4 | API | $2.50 in / $15.00 out | Ask the Loremaster, continuity audit, continuity fix, storyline retrospective, world-name generation |
| `nornis-embed` | embedding | both | $0.02 in | library indexing, passage retrieval |

Sources: `src/Nornis.Worker/appsettings.json` (`Extraction` block), `src/Nornis.Api/appsettings.json` (`Loremaster` block), `src/Nornis.Application/Configuration/LibraryOptions.cs:44-50`.

Note the config comment at `src/Nornis.Api/appsettings.json` `Loremaster._aiModelNote`: `nornis-ask` was **upgraded from gpt-5.4-mini to full gpt-5.4 on 2026-07-16 for Ask answer quality** — and that single ChatClient is shared by four other features that never asked for the upgrade (`src/Nornis.Api/Program.cs:186-191`). That decision is the root of findings #7 and part of #4.

**Cost telemetry is good.** Every model call except world-name generation writes an `AiUsageRecord` with input/output/total tokens, model, duration, success, and a computed USD cost (`AiOperationType` has 12 members). `CostService` aggregates by day/week/month/all-time and by operation type. This is better than most codebases and made this audit possible.

## Public Ask spend cap — VERIFIED ENFORCED

Asked specifically, so stated plainly: **the per-world monthly cap on public Ask exists, is enforced before the model call, and is not trivially bypassable.**

- Cap lives on the world row as `PublicAskMonthlyBudgetUsd`; `AiBudgetGuard.GetPublicAskStatusAsync` (`src/Nornis.Application/Services/AiBudgetGuard.cs:51-69`) treats a non-positive cap as "public Ask is off", so the cap doubles as the feature switch — it cannot be left enabled with no ceiling.
- `PublicController.Ask` calls `CheckPublicAskAsync` at `src/Nornis.Api/Controllers/PublicController.cs:86`, and returns before reaching `_loremasterService.AskAsync` at `:100`. **Gate precedes spend.**
- Spend is metered by `SumPublicAskCostAsync` (`src/Nornis.Infrastructure/Persistence/Repositories/AiUsageRecordRepository.cs:93-108`) over `AskLoremaster` rows with `UserId == null` since month start — anonymous asks are the only rows with a null user, so the metering is sound.
- Defence in depth: a second **daily** world budget check fires inside `LoremasterService.AskAsync` at `src/Nornis.Application/Services/LoremasterService.cs:126`, before any retrieval; and a per-IP fixed-window limiter of **5 requests/minute** is attached via `[EnableRateLimiting("public-ask")]` (`PublicController.cs:77`, policy at `src/Nornis.Api/Program.cs:278-286`).
- Public asks correctly opt out of library retrieval (`IncludeLibrary: false`, `PublicController.cs:98`) and carry no conversation context (`:97`).

The only weakness is a check-then-act race (finding #14, Low): N concurrent asks all read the same pre-call spend total and all proceed. With a 5/min/IP limiter the realistic overshoot is a handful of asks, and finding #1 is what makes each of those asks expensive.

---

# Findings

Ordered by dollar impact ÷ effort — best payoff first.

### [SEVERITY: High] Every Ask stuffs up to three complete session documents into the prompt, untruncated
- **Where:** `src/Nornis.Infrastructure/Knowledge/KeywordKnowledgeRetriever.cs:188-197` (`MapSession`), rendered at `src/Nornis.Application/Services/LoremasterService.cs:458-472`
- **What:** `MapSession` sets `Text = session.Body ?? session.DerivedText` with **no length limit**. `LoremasterOptions.RecentSessionCount` defaults to 3 (`src/Nornis.Application/Configuration/LoremasterOptions.cs:20`), and `FormatKnowledgeContext` writes each session's full text into the prompt. Source bodies are validated up to **100,000 characters** (`src/Nornis.Application/Services/SourceService.cs:504-506`). Sessions are fetched **unconditionally**, even when the question has nothing to do with recency — the comment at `KeywordKnowledgeRetriever.cs:57-59` confirms this is deliberate.
- **Why it costs:** Worst case 3 × 100,000 chars ≈ **75,000 input tokens per ask** = **$0.19 per question** in session text alone. A realistic 8,000-char session note gives 3 × 2,000 ≈ 6,000 tokens ≈ $0.015/ask — still the single largest block in the prompt, dwarfing the 1,300-token system prompt. This fires on every authenticated ask *and* every anonymous public ask. It is also why the $2.00 default daily world budget can be consumed by roughly **10 questions** in a world with long session notes.
- **Fix:** Cap the per-session text and prefer the head of the note (where session summaries live):
  ```csharp
  // KeywordKnowledgeRetriever.MapSession
  private const int MaxSessionChars = 4_000;
  Text = Truncate(string.IsNullOrWhiteSpace(session.Body) ? session.DerivedText : session.Body, MaxSessionChars);
  ```
  Better still, make it a `LoremasterOptions.MaxSessionChars` so it is tunable without a deploy, and consider only attaching session text when the question is time-anchored.
- **Effort:** trivial
- **Risk:** Quality regression on "what happened last session?" if the cut is too aggressive or lands mid-sentence — truncate on a paragraph boundary and append an explicit "[session record truncated]" marker so the model knows the record continues rather than silently answering from a partial note.

### [SEVERITY: High] `MaxContextTokens` is dead config — nothing enforces a token ceiling on the Ask prompt
- **Where:** `src/Nornis.Application/Configuration/LoremasterOptions.cs:14`, set to `8000` in `src/Nornis.Api/appsettings.json`
- **What:** `grep -rn "MaxContextTokens" --include=*.cs src/` returns **exactly one hit — the property declaration**. It is read by nothing. `LoremasterService.BuildPrompt` (`:390-426`) and `FormatKnowledgeContext` (`:431-546`) concatenate artifacts, facts, relationships, source-reference quotes, library passages and session text with no running token count and no truncation anywhere.
- **Why it costs:** The intended 8,000-token ceiling would have capped an ask at ~$0.02 input. With the ceiling unimplemented, the actual prompt is bounded only by the individual retrieval caps (`MaxRetrievalCount: 30` artifacts × `MaxFactsPerArtifact: 15` facts, `MaxContextPassages: 12` library passages, 3 uncapped sessions). Finding #1 alone can put an ask an order of magnitude over the intended budget. There is currently **no upper bound on the cost of a single question**, which is exactly the guarantee the public Ask cap needs in order to be predictable.
- **Fix:** Implement the budget in `FormatKnowledgeContext`: build sections in priority order (sessions → artifacts+facts → relationships → quotes → passages), keep a running `chars/4` estimate, and stop appending once `MaxContextTokens` is reached, appending a "[context truncated]" note. A rough char/4 heuristic is fine; precision is not the point, a ceiling is.
- **Effort:** small
- **Risk:** Truncation drops context the model would have cited, so answers can get thinner in large worlds. Order sections by value so the first things cut are the least useful (source-reference quotes and neighbour-expanded passages), and log when truncation fires so you can tell whether the cap is biting.

### [SEVERITY: High] Extraction's user message is ordered backwards for prompt caching — the stable world catalog sits last
- **Where:** `src/Nornis.Infrastructure/Ai/AzureOpenAiExtractionClient.cs:336-421` (`BuildUserMessage`)
- **What:** The message is assembled as: source metadata → location context → **Source Content** (`:370-372`) → published reference passages (`:374-384`) → **Existing World Artifacts** (`:386-418`). The single most volatile block — this source's body, unique to every call — is placed near the front. The most *stable* block — the world's artifact catalog with `MaxArtifactContextCount: 50` artifacts each carrying up to `MaxFactsPerArtifact: 20` facts — is placed last.
- **Why it costs:** Azure OpenAI prompt caching matches on a **longest common prefix**. The extraction system prompt (~11,200 chars ≈ **2,800 tokens**, `:192-333`) is stable and does cache. But the instant the user message begins, the differing source body breaks the prefix, so **the entire artifact catalog is re-billed at full rate on every single extraction**. That catalog runs roughly 50 artifacts × (~200 chars header+summary + 20 facts × ~80 chars) ≈ **90,000 chars ≈ 22,500 tokens**. At $2.50/M that is **~$0.056 per extraction of pure re-billed, cacheable content**. A GM importing 200 notes into one world pays that 200 times over — **~$11 that caching would have largely eliminated**. It also compounds finding #6, since every parse retry resends the same block.
- **Fix:** Invert the section order in `BuildUserMessage` so the world-stable content leads and the per-source content trails:
  ```
  ## Existing World Artifacts    <- stable across every source in this world
  ## Published Reference          <- stable per world/shelf
  ## Source Information           <- per source
  ## Location Context             <- per source
  ## Source Content               <- most volatile, last
  ```
  Nothing in the system prompt depends on section order — it refers to sections by name ("The user message lists artifacts the world already knows", `:298`), not position. Also stabilise the artifact ordering (it comes from `AssembleContextAsync`'s name-matched-then-recent merge, `src/Nornis.Application/Services/ExtractionService.cs:947-971`, which reorders as the world changes); sorting by artifact Id would make the prefix byte-identical far more often.
- **Effort:** small
- **Risk:** Low mechanically, but non-zero on quality: models weight late context more heavily, so moving the source body last may actually *help* extraction focus, while moving the catalog first could slightly reduce duplicate-detection sensitivity. Worth a side-by-side on a handful of real sources before rolling out.

### [SEVERITY: High] Continuity audit sends the entire world, uncapped, on an hourly sweep, with no unchanged-content guard
- **Where:** `src/Nornis.Application/Services/ContinuityAuditService.cs:134-161` (load) and `:647-738` (`FormatWorldRecord`); trigger at `src/Nornis.Api/BackgroundServices/ContinuityAuditBackgroundService.cs`, registered `src/Nornis.Api/Program.cs:176`
- **What:** Three compounding problems.
  1. **The payload is uncapped.** Artifacts via `ListByWorldAsync(worldId, null, null, ct)` (`:136`) — no `Take` in the repository. Relationships (`:149-153`) — **no cap of any kind**. Sources for the timeline (`:161`) — no cap. Only facts are bounded, and only *per artifact* (`MaxFactsPerArtifactInAudit = 25`, `:45`), so the worst case is artifacts × 25. `MaxFindings = 20` (`:38`) and `MaxQuotesInAudit = 100` (`:46`) bound the output and the quote list, not the record itself.
  2. **It runs on a timer across every world.** `TickIntervalHours` defaults to 1.0 (`src/Nornis.Application/Configuration/ContinuityAuditOptions.cs:11`) and **no `ContinuityAudit` section exists in either appsettings**, so the defaults are live. Each tick sweeps `ListWorldIdsWithAcceptancesAsync` — every world that has ever had one accepted proposal, no date bound, no paging — and audits each eligible one sequentially. Eligibility is time-based only: 1 h quiet period, 20 h minimum interval. So **every active world gets a full-world audit roughly daily, forever, whether or not anything changed.**
  3. **No content-hash guard.** Nothing compares the world record against the previous assessment's inputs. The only output-side dedup (`ApplyDismissalRegistry`, `:312-335`) runs *after* the model has been paid.
- **Why it costs:** A mature world with 300 artifacts, 25 facts each and 400 relationships renders on the order of **40,000–60,000 input tokens ≈ $0.10–$0.15 per audit**, once a day, per world, unattended. Across 50 active worlds that is **~$5/day ≈ $150/month of fully automatic background spend** that no user requested, much of it re-analysing a record that has not materially changed since yesterday. It also silently eats the $2.00 daily world budget before the GM's own extractions and asks get a chance at it.
- **Fix:** Three changes, in payoff order:
  1. **Add a content fingerprint.** Hash the rendered `FormatWorldRecord` output (or a cheap composite of max `UpdatedAt` + row counts across artifacts/facts/relationships), store it on `HealthAssessment`, and skip the run when it matches the last assessment. This alone removes most of the recurring spend, because the common case is "nothing changed since yesterday".
  2. **Cap the record.** Give relationships and timeline sources explicit `Take` limits alongside the existing fact cap, and cap total artifacts. Prefer recently-changed artifacts — a continuity contradiction is far likelier to involve something touched recently.
  3. **Bound the sweep.** Cap worlds audited per tick, and add jitter so a restart does not audit every world at once.
- **Effort:** medium
- **Risk:** The fingerprint is the delicate one — hash too coarsely and real changes get skipped, so the GM stops getting findings and never notices. Hash the actual rendered prompt text rather than a proxy. Capping the record can hide a contradiction between an old artifact and a new one, which is precisely the class of finding the audit exists to catch — cap generously and prefer dropping timeline entries over dropping relationships.

### [SEVERITY: High] A transient failure or a re-index re-embeds the entire document from scratch
- **Where:** `src/Nornis.Application/Services/LibraryIndexingService.cs:58-134`; redelivery at `src/Nornis.Worker/LibraryIndexingWorker.cs:87-94`; re-index entry point `src/Nornis.Application/Services/LibraryService.cs:294`
- **What:** `ProcessIndexingAsync` runs blob → PDF text → chunk → embed → `ReplaceForDocumentAsync`. The embedding loop (`:97-117`) walks `chunks.Chunk(EmbedBatchSize)` and accumulates all writes in memory, committing **only after every batch completes** (`:119`). On a transient failure the service returns `ExtractionOutcome.Transient` (`:135-139`), the worker abandons the message (`LibraryIndexingWorker.cs:92`), Service Bus redelivers, and the document is still in `Indexing` status — so the whole pipeline restarts **from chunk zero**. There is **no content hash anywhere in the codebase** (`grep -rn "ContentHash\|Sha256\|Checksum" --include=*.cs src/` returns nothing) and no resume point.
- **Why it costs:** A 400-page rulebook at `MaxChunkChars: 3200` with `OverlapChars: 480` produces roughly 700–900 chunks ≈ 700k tokens ≈ **$0.014 per full index pass** at $0.02/M. Cheap per pass — but a rate-limit blip at batch 12 of 14 re-pays all 12, and Service Bus will retry to `MaxDeliveryCount`. The sharper edge is `ReindexAsync`: an unchanged PDF re-embedded on a GM's click costs the full amount again, every time, with zero new information. Embedding cost is genuinely low here, so this is High on *correctness of the cost model* and on wasted wall-clock/rate-limit budget more than on absolute dollars — but it is the same missing primitive (a content hash) that finding #4 needs, which is why fixing it once pays twice.
- **Fix:** Store a SHA-256 of the extracted text (and of the chunking parameters) on `LibraryDocument`. In `ReindexAsync`, short-circuit when the hash and parameters match an already-`Indexed` document. For redelivery resilience, persist chunk writes incrementally per batch rather than accumulating all of them, and record the highest completed `Ord` so a resumed run skips what is already embedded.
- **Effort:** small for the re-index hash; medium for incremental/resumable batches
- **Risk:** A stale hash means a genuinely-changed document silently keeps old embeddings and the library answers from outdated text — include the chunker settings and the embedding deployment name in the hash input, and always provide a force-reindex escape hatch for the GM.

### [SEVERITY: Medium] Extraction re-sends the full prompt up to three times because the JSON schema is deliberately non-strict
- **Where:** `src/Nornis.Application/Services/ExtractionService.cs:1054-1139`; schema mode at `src/Nornis.Infrastructure/Ai/AzureOpenAiExtractionClient.cs:429-437`
- **What:** `InvokeAiWithRetriesAsync` builds `request` **once** (`:1054`) and then loops `maxAttempts = 1 + MaxParseRetryAttempts` (= **3**, per `src/Nornis.Worker/appsettings.json`), calling `_aiExtractionClient.ExtractAsync(request, ct)` with the **identical request object** on each attempt (`:1064`). Retries fire on `AiExtractionParseException` and on `ValidateResponse` failures. The parse is strict client-side: `ParseProposal` throws on an unknown `changeType`, an out-of-range confidence, a rationale over 500 chars (`:557-561`), or a non-UUID `targetId`. Meanwhile the server-side schema is explicitly **not** strict (`jsonSchemaIsStrict: false`) because `proposedValue` is an open object — the comment at `AzureOpenAiExtractionClient.cs:432-436` documents the tradeoff. So the model is free to emit shapes the client then rejects, and each rejection buys a full re-run. The same pattern exists in the map path (`ExtractionService.cs:471-532`).
- **Why it costs:** A retried extraction re-pays input (partially discounted if Azure's automatic prefix cache hits — it should, since the prompt is byte-identical and the retry is immediate) plus **output at full rate, $15/M, with no discount available**. A 50-proposal response is easily 6,000–10,000 output tokens ≈ $0.09–$0.15 **per wasted attempt**. Two wasted attempts on a rich source approach **$0.30 thrown away**, and finding #3 means the 22,500-token catalog rides along each time.
- **Fix:** Two parts.
  1. **Do not re-call the model for defects a local fix handles.** A rationale over 500 chars is currently a hard throw (`:557-561`) — truncate it, exactly as the code already does for an over-long `quote` (`:585-588`). Same for a confidence marginally outside 0–1 (clamp) and for an unknown-but-harmless field. Reserve the retry for genuinely unparseable JSON.
  2. **Split the schema so strict mode becomes usable.** A `oneOf` over the seven `changeType` payload shapes would let `jsonSchemaIsStrict: true` do the enforcement server-side, where a malformed response is never generated (and never billed) in the first place. Larger change, but it removes the whole failure class.
- **Effort:** small for the local-fix part; medium for the strict schema
- **Risk:** Truncating a rationale instead of rejecting the proposal means the reviewer occasionally sees a clipped explanation — strictly better than losing the entire extraction to an exhausted retry budget. Loosening validation too far would let genuinely bad proposals through to the review queue, so keep the hard throws for structural problems (bad `changeType`, non-UUID id) and soften only the cosmetic ones.

### [SEVERITY: Medium] World-name generation burns the premium Ask model on a two-word string, and the spend is untracked
- **Where:** `src/Nornis.Infrastructure/Ai/AzureOpenAiWorldNameGenerator.cs:28-55`
- **What:** Demo-world creation asks for one fantasy world name — `MaxOutputTokenCount = 20` (`:44`). It reuses the injected Loremaster `ChatClient`, which since 2026-07-16 is **full gpt-5.4** at $2.50/$15.00. It is also the **only** model call in the codebase that never writes an `AiUsageRecord`, and it is never gated by `IAiBudgetGuard`.
- **Why it costs:** Per call the dollars are negligible (~150 input + 20 output tokens ≈ $0.0007). Two things make it worth listing anyway. First, it is a textbook case of the shared-client problem: `src/Nornis.Api/Program.cs:186-191` hands one premium `ChatClient` to five features, so a quality upgrade made for Ask silently repriced name generation, continuity audit, continuity fix and retrospective too. Second, **untracked spend is invisible spend** — this call cannot appear in `CostService`, cannot count against any budget, and cannot be found when reconciling the Azure bill against `AiUsageRecords`. `DemoWorldOptions.MaxCreationsPerUserPerDay: 1` is the only thing bounding call volume.
- **Fix:** Register a second, cheap `ChatClient` (a `nornis-mini` deployment) as a keyed service and inject it here — the same keyed-service pattern the worker already uses for its second queue processor (`src/Nornis.Worker/Program.cs`). Add an `AiUsageRecord` write with a new `AiOperationType.WorldNameGeneration` so the row exists even at $0.0007. This also sets up the pattern for moving continuity audit and retrospective off the premium deployment.
- **Effort:** small
- **Risk:** Low. A smaller model producing blander world names is a cosmetic regression on a feature that already falls back to static names on any failure (`:50-53`).

### [SEVERITY: Medium] Map extraction fires up to nine extra vision calls per map, on by default
- **Where:** `src/Nornis.Infrastructure/Ai/AzureOpenAiMapExtractionClient.cs:80-87` and `:131-201`; grid at `src/Nornis.Infrastructure/Ai/MapRefinement.cs:16` (`Grid = 3`)
- **What:** After the whole-map read, `RefinePlacesAsync` buckets places into a 3×3 grid and issues **one additional vision call per occupied tile**, sequentially (`:149-177`). `ExtractionOptions.MapRefinePositions` defaults to **`true`** (`src/Nornis.Application/Configuration/ExtractionOptions.cs:14`). Each refinement call carries a freshly-encoded PNG crop (`MapRefinement.CropTiles`) plus a fresh system prompt.
- **Why it costs:** A well-labelled map occupies all nine tiles, so one map upload becomes **up to 10 vision calls** instead of 1. Image input tokens dominate: a large crop can run 1,500–3,000 tokens, so refinement adds roughly **15,000–27,000 input tokens ≈ $0.04–$0.07 per map** on top of the base call. Because the tiles are awaited in sequence, it also multiplies wall-clock latency ~10× against a 60-second per-call timeout.
- **Fix:** The cheap win is to make refinement conditional rather than unconditional — skip it when the first pass returns few places (a 4-place map does not need tile-level precision), and consider `Grid = 2` (4 tiles) as the default. The tiles are independent, so running them with a bounded `Parallel.ForEachAsync` (degree 2–3) would cut latency without changing spend. Downgrading the default to `false` and letting GMs opt in per map is the zero-risk version.
- **Effort:** trivial (config default) to small (conditional + bounded parallelism)
- **Risk:** Pin accuracy is the whole point of the second pass — the comment at `:76-79` says first-pass positions are "good enough to find each place, too sloppy to pin it". Degrading it universally would visibly worsen map placemarks, so make it conditional rather than simply switching it off.

### [SEVERITY: Medium] Relationship backfill re-sends the whole storyline+event catalog once per source
- **Where:** `src/Nornis.Application/Services/RelationshipBackfillService.cs:443-501` (`BuildUserMessage`), fan-out at `src/Nornis.Application/Services/RelationshipBackfillQueueService.cs:42-60`
- **What:** One GM click enqueues **one message per eligible source** (every `Processed` source with a non-empty body). Each message's prompt contains every visible Storyline with summary and parent (`:448-458`), every visible Event with summary (`:466-473`), every existing Advances/PartOf link (`:486-489`), and **the entire source body uncapped** (`:498`). There are **no cap constants in this file at all** — it uses neither `MaxArtifactContextCount` nor `MaxFactsPerArtifact`, both of which the ordinary extraction path respects.
- **Why it costs:** The catalog is identical across every message in the sweep, so a world with 150 sources and a 120-storyline/event catalog (~15,000 tokens) re-bills that catalog **150 times ≈ 2.25M input tokens ≈ $5.60 per sweep** — for content that never changes during the sweep. Add the uncapped source bodies on top. Per-source idempotency exists (`ExistsForSourceAsync(sourceId, BatchKind)`, `:80-83`) so a *re-run* is cheap, but the first sweep is not, and the caps that protect normal extraction are simply absent here.
- **Fix:** Apply the same prefix-ordering fix as finding #3 — catalog first, source body last — so Azure's automatic prefix cache absorbs the repeated catalog across the sweep. That is the single highest-leverage change and it is nearly free once #3 establishes the pattern. Then add explicit caps on storylines/events (reuse `MaxArtifactContextCount`) and truncate the source body.
- **Effort:** medium
- **Risk:** Capping the catalog means a link to an omitted storyline never gets proposed. Prefer Active storylines and recent Events when trimming, and note that these are proposals a human reviews, so a miss is a smaller harm than a wrong accept.

### [SEVERITY: Medium] No output-token ceiling on any call except world-name generation
- **Where:** every client in `src/Nornis.Infrastructure/Ai/` except `AzureOpenAiWorldNameGenerator.cs:44`
- **What:** `grep -rn "MaxOutputTokenCount|Temperature" --include=*.cs src/` returns exactly **one** hit, in the name generator. Extraction, Ask, audit, continuity fix, retrospective, backfill, handwriting transcription, image reading and map extraction all leave `MaxOutputTokenCount` unset, so each is bounded only by the deployment default and the wall-clock timeout. Two clients pass `options: null` entirely (`AzureOpenAiHandwritingTranscriptionClient.cs:53`, `AzureOpenAiImageReadingClient.cs:54`).
- **Why it costs:** Output is **$15.00/M — six times the input rate**, so it is the expensive direction and the one with no guardrail. A degenerate or repetitive generation runs until the deployment cap or the timeout, and the tokens are billed regardless of whether the response is then discarded by a parse failure. The exposure is worst where responses are legitimately long: extraction (up to 50 proposals, each with rationale and quote) and handwriting transcription (multi-page verbatim output).
- **Fix:** Set a deliberate `MaxOutputTokenCount` per call site sized to the real response: extraction ~16,000 (50 proposals), Ask ~1,500 (the system prompt already asks for "a few short paragraphs at most", `LoremasterService.cs:91`), audit ~4,000 (20 findings), retrospective ~4,000 (40 verdicts), name generation already 20. A truncated response surfaces as a parse failure rather than a silent overcharge, which is the behaviour you want.
- **Effort:** trivial
- **Risk:** Set a ceiling too low and legitimate long responses get cut mid-JSON, converting an expensive success into a retry loop (finding #6). Size each from observed p99 output tokens in `AiUsageRecords` — the telemetry to do this is already there.

### [SEVERITY: Medium] Storyline retrospective checks the budget once, then makes N model calls
- **Where:** `src/Nornis.Application/Services/StorylineRetrospectiveService.cs:67-71` (guard) and `:91-102` (loop)
- **What:** `_budgetGuard.CheckAsync` runs **once**, before the loop. The loop is `foreach (var chunk in storylines.Chunk(ChunkSize))` with `ChunkSize = 40` (`:16`), one model call per chunk, sequential. The budget is never re-checked between chunks. Separately, `:84-87` loads facts with one `ListByArtifactAsync` round-trip **per storyline** (an N+1) and applies **no per-storyline fact cap** — unlike every comparable path.
- **Why it costs:** A world with 200 active storylines issues 5 sequential calls after a single budget check, so one run can overshoot the daily ceiling by up to (chunks − 1) calls. If a mid-loop call throws, `:104-110` aborts the whole run with a 502 and **discards the verdicts from chunks already paid for** (persistence happens only after the loop, `:129`) — a total loss of everything spent so far.
- **Fix:** Re-check `_budgetGuard.CheckAsync` at the top of each chunk iteration and stop cleanly when exceeded. Persist verdicts per chunk (or accumulate and persist what succeeded on abort) so a late failure does not discard earlier paid-for work. Add a per-storyline fact cap mirroring `MaxFactsPerArtifactInAudit`.
- **Effort:** trivial for the re-check; small for partial persistence
- **Risk:** Partial persistence means a GM can end up with a half-assessed batch. Mark it clearly in the batch so it reads as "assessed 120 of 200 storylines, budget reached" rather than silently looking complete.

### [SEVERITY: Medium] The extraction worker processes strictly one message at a time
- **Where:** `src/Nornis.Worker/appsettings.json` → `ServiceBus.MaxConcurrentCalls: 1`, `PrefetchCount: 0`; consumed at `src/Nornis.Worker/Program.cs` (`ServiceBusExtractionProcessor` registration)
- **What:** Both queue processors — extraction and library indexing — are constructed from the same `WorkerOptions`, so `MaxConcurrentCalls: 1` applies to both. Extraction is overwhelmingly I/O-bound waiting on a model call with a 60-second timeout.
- **Why it costs:** No dollar cost — a latency and throughput cost. A 200-note import runs strictly serially; at ~15 s per extraction that is **~50 minutes of wall clock** where a concurrency of 4 would be ~13. The relationship-backfill sweep (finding #9) inherits the same serialisation. This is the *opposite* failure mode from unbounded parallelism, and given `AiBudgetGuard` bounds spend independently, modest concurrency is safe here.
- **Fix:** Raise `MaxConcurrentCalls` to 3–4 and set `PrefetchCount` to roughly 2× that. Each message already gets its own DI scope and `DbContext` (`ExtractionWorker.cs:98-100`), so this is safe on the persistence side. Watch for Azure OpenAI 429s as you raise it — the transient path already abandons for redelivery, so throttling degrades gracefully rather than losing work.
- **Effort:** trivial
- **Risk:** Higher concurrency means more simultaneous 429s and more redeliveries, and each redelivery re-pays the full prompt (finding #6). Raise it gradually and watch the `RateLimited`/`TransientError` counts in `AiUsageRecords` — if redeliveries climb, the extra concurrency is costing money rather than saving time.

### [SEVERITY: Low] Ask is fully buffered — no streaming
- **Where:** `src/Nornis.Infrastructure/Ai/AzureOpenAiLoremasterClient.cs:43-45`; confirmed by grep — `CompleteChatStreamingAsync` appears nowhere in `src/`
- **What:** All 11 model call sites use the buffered `CompleteChatAsync`. The user waits for the complete answer with no partial output.
- **Why it costs:** No dollar impact — token spend is identical. It is a perceived-latency cost, and it is concentrated exactly where users notice: Ask is the one interactive, human-in-the-loop AI feature (`Ask.razor`, `LoremasterPanel.razor`), and with a 30-second timeout and a large context (findings #1 and #2) a first token can be many seconds away. Every other call site is background work where buffering is the right call.
- **Fix:** Stream only the Loremaster path. Note two real complications: `ParseCitations` (`LoremasterService.cs:569-603`) runs a regex over the complete answer text, and `TrackUsageAsync` needs the final usage object — both need the stream fully accumulated before they run, so streaming is presentation-layer only and the usage record must be written on stream completion.
- **Effort:** medium
- **Risk:** Citations are currently resolved against the finished text; streaming raw text to the UI means `[ref:...]` markers appear before they are resolved to display names. Render them as placeholders and swap on completion, or the answer will visibly flicker.

### [SEVERITY: Low] The public Ask cap is check-then-act, so concurrent asks can overshoot it
- **Where:** `src/Nornis.Api/Controllers/PublicController.cs:86`; spend read at `src/Nornis.Application/Services/AiBudgetGuard.cs:62`
- **What:** `CheckPublicAskAsync` sums prior spend and returns a decision; the `AiUsageRecord` for the current ask is not written until after the model returns (`LoremasterService.cs:254`). N requests arriving inside that window all read the same total and all pass. The identical pattern applies to the daily `CheckAsync` on every other AI path.
- **Why it costs:** Bounded and small in practice. The `public-ask` limiter allows **5 requests/minute per IP** (`src/Nornis.Api/Program.cs:278-286`), so a single-source burst overshoots by at most a few asks. A distributed burst across many IPs could do somewhat better, but the daily world budget check inside `AskAsync` (`:126`) provides a second, independent backstop. What makes any overshoot matter is finding #1 — at up to $0.19/ask the overshoot is worth real money; fix #1 and this shrinks to noise.
- **Fix:** Not worth a distributed lock. The cheap mitigation is a soft margin — treat the cap as reached at, say, 95% of the ceiling — which absorbs in-flight requests without new infrastructure. Fix #1 and #2 first; they cap the blast radius far more effectively.
- **Effort:** trivial
- **Risk:** A margin means the GM's configured cap is effectively slightly lower than the number they typed. Say so in the UI copy, or GMs will report it as a bug.

### [SEVERITY: Low] Ask conversation history is windowed to 5 exchanges but individual answers are never truncated
- **Where:** `src/Nornis.Web/Components/Pages/Ask.razor` (`BuildContext`) and `src/Nornis.Web/Components/Shared/LoremasterPanel.razor:232-245`
- **What:** Both UIs build the conversation context with `c.Exchanges.TakeLast(5)`, appending each `Q:` and full `A:`. The **count** is bounded — history does not grow without limit, which is the important part and is done correctly. Individual answer text is not truncated.
- **Why it costs:** Small and self-limiting. The Loremaster system prompt instructs "a few short paragraphs at most" (`LoremasterService.cs:91`), so five exchanges typically run 1,500–3,000 tokens ≈ $0.004–$0.008 per follow-up. Worth noting only because it rides on top of findings #1 and #2 on the same request, and because the context is sent twice per ask — once for retrieval name-matching (`LoremasterService.cs:136-138`) and again in the prompt (`:407-412`).
- **Fix:** Truncate each stored answer to ~600 chars when building context, or drop to `TakeLast(3)`. Handle it in `FormatKnowledgeContext`'s token budget (finding #2) rather than in the UI, so the server enforces its own ceiling instead of trusting a client-supplied string — the current design lets any API caller pass an arbitrarily long `ConversationContext`.
- **Effort:** trivial
- **Risk:** Truncated history degrades pronoun resolution on long follow-up chains ("what about his brother?"), which the system prompt explicitly relies on (`:96-97`). Keep questions verbatim and truncate only answers.

### [SEVERITY: Low] `TickIntervalHours: 0` produces a hot loop rather than disabling the audit
- **Where:** `src/Nornis.Api/BackgroundServices/ContinuityAuditBackgroundService.cs:35`
- **What:** The interval is computed as `TimeSpan.FromHours(Math.Max(0.0, _options.TickIntervalHours))`. `Math.Max(0.0, ...)` guards against a *negative* value but maps `0` to a **zero delay**, so the sweep loop spins continuously.
- **Why it costs:** Zero today — no `ContinuityAudit` section exists in either appsettings, so the 1.0-hour default is live. This is a latent footgun, not a live cost. But if someone sets `0` intending "off" (a natural reading), the result is a continuous sweep of every world in the database, each tick gated only by the 20-hour per-world eligibility window. The budget guard caps the *spend* per world, but the DB load is unbounded and the intent is inverted.
- **Fix:** Treat non-positive as disabled, which is the convention `AiBudgetOptions` already uses for its own budget (`"Zero or negative disables the guard"`, `src/Nornis.Application/Configuration/AiBudgetOptions.cs:8-11`):
  ```csharp
  if (_options.TickIntervalHours <= 0) return; // disabled
  ```
- **Effort:** trivial
- **Risk:** None — it makes the option behave the way its sibling option already documents.

---

## Unverified / worth a look

Things I could not confirm from source alone, kept separate from the findings above.

- **Azure automatic prompt caching eligibility.** Findings #3 and #9 assume Azure OpenAI applies automatic prefix caching above a ~1,024-token threshold with a discounted rate on cached input. I confirmed the *code-side* facts — prompt sizes, section ordering, byte-identical retries — but not that this specific `gpt-5.4` deployment is caching-eligible or at what discount. Confirm on the resource's pricing blade, then check `AiUsageRecords` for the cached-token split (the current `AiUsageRecord` schema has `InputTokens`/`OutputTokens` only, so **cached vs uncached input is not currently observable** — worth adding a `CachedInputTokens` column before/after any reordering, or you will not be able to measure whether the fix worked).
- **`Temperature = 1.2f` on gpt-5.4.** `AzureOpenAiWorldNameGenerator.cs:44` sets it. Some newer reasoning-family deployments reject non-default `temperature` with a 400. The call is wrapped in a catch-all that returns null and falls back to static names (`:50-53`), so a rejection would be **completely silent** — the memory note says both observed prod fallbacks on 2026-07-25/26 were attributed to 3-second timeouts, but a 400 would look identical from outside. Worth checking the logs for the actual status code.
- **Actual per-world artifact and session counts in production.** All dollar estimates above are parameterised on world size. The `AiUsageRecords` table already has real `InputTokens` per operation type — `AggregateByOperationTypeAsync` would give exact per-operation averages and turn every estimate here into a measurement. That is the fastest way to rank findings #1, #4 and #9 against each other by real spend.
- **Service Bus `MaxDeliveryCount`.** Governs how many times a transient failure re-pays a full prompt (findings #5, #9). Configured queue-side in Azure, not in this repo — `ExtractionWorker.cs:192-193` explicitly defers dead-lettering to it. Check the actual value; a high one turns a transient outage into repeated full-price retries.
- **Embedding call concurrency under load.** `LibraryIndexingService` batches at `EmbedBatchSize: 64` and awaits sequentially, which is safe. I did not verify Azure's rate limit on the `nornis-embed` deployment, so I cannot say whether raising batch concurrency would help or immediately throttle.

## What is already right

Worth recording so a future pass does not "fix" these:

- **Embeddings are properly batched** — `AzureOpenAiEmbeddingClient.EmbedAsync` takes `IReadOnlyList<string>` and the indexing service feeds it 64 chunks per call (`LibraryIndexingService.cs:97`). No one-at-a-time embedding anywhere.
- **Vision reads are batched into a single call** — handwriting transcription and image reading each send all pages/images as content parts in one request rather than one call per image.
- **Expensive derived text is persisted before extraction** so redelivery never re-buys it — `UpdateBodyAsync` after transcription (`ExtractionService.cs:369`) and `UpdateDerivedTextAsync` after vision (`:864`), both with explicit comments saying exactly why.
- **Extraction idempotency is genuinely careful** — the `ReviewBatch` existence check (`:150`) proves completion even when a crash landed before the status write, and crashed-mid-run sources are resumed rather than skipped.
- **`ReferencePassageRetriever` skips the embedding call entirely** when the world has no indexed documents in scope (`:67-70`), so ordinary asks pay nothing for the library feature.
- **Cost telemetry is comprehensive** — 12 operation types, per-call token and USD capture, budget guard on every path but one. This is the foundation that makes everything above measurable.
