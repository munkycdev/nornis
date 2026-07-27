# Audit 07 — Worker runtime, build, and hosting

Scope: `src/Nornis.Worker/`, Dockerfile, compose, project files, `scripts/`, CI workflows,
test-suite runtime. Read-only. Every finding below was confirmed by reading the cited file.

**Baseline that is already right** (so it doesn't get "fixed" by accident): the work loop is
genuinely event-driven off `ServiceBusProcessor` (peek-lock, `AutoCompleteMessages = false`,
`Task.Delay(Timeout.Infinite, stoppingToken)` — no timer, no polling, no idle CPU burn); a DI
scope is created per message, not per loop lifetime (`ExtractionWorker.cs:100`,
`LibraryIndexingWorker.cs:81`); undeserializable poison messages are *completed*, not abandoned,
so there is no infinite poison loop (`ExtractionWorker.cs:73,85`); extraction idempotency is
real and well thought through (ReviewBatch-first check at `ExtractionService.cs:148-171`,
persisted transcription/derived-text so a redelivery never re-buys the vision call at
`ExtractionService.cs:196,216,367`); and a daily per-world AI budget guard caps the blast radius
of any retry storm (`AiBudgetGuard.cs:44`). The findings are about the edges around that core.

Counts: **8 High**, **8 Medium**, **7 Low**.

---

## High

### [SEVERITY: High] The `library-indexing` queue is never provisioned, and KEDA never scales for it
- **Where:** `scripts/provision-azure.ps1:127-141` (worker app + scale rule),
  `scripts/servicebus-emulator.json:7-20`, vs `src/Nornis.Infrastructure/Messaging/ServiceBusLibraryIndexingQueueClient.cs:9`
  and `src/Nornis.Worker/Program.cs:183-196`.
- **What:** `Program.cs` registers a second keyed `ServiceBusExtractionProcessor` on queue
  `library-indexing` and a second hosted service for it. The provisioning script creates an
  authorization rule and a KEDA scale rule for **only** `$Queue = "source-extraction"`
  (`--scale-rule-metadata "queueName=$Queue" "messageCount=1"`). It never creates the
  `library-indexing` queue. The local Service Bus emulator config declares only
  `source-extraction` as well.
- **Why it costs:** Two ways, both bad. If the queue doesn't exist,
  `StartProcessingAsync` throws `MessagingEntityNotFound` out of `LibraryIndexingWorker.ExecuteAsync`,
  which under the default `BackgroundServiceExceptionBehavior.StopHost` takes the **whole worker
  process** down — so extraction stops too, and a `--min-replicas 0` Container App restarts into
  the same crash on every queue-triggered wake (image pull + startup billed each time, no work
  done). If the queue was created by hand in prod, the scale rule still doesn't watch it: with
  `--min-replicas 0`, an uploaded PDF sits in `library-indexing` indefinitely until an unrelated
  extraction message happens to wake the worker.
- **Fix:** Add the queue to both configs, and add a second scale rule:
  ```powershell
  az servicebus queue create -g $ServiceBusRg --namespace-name $ServiceBusNamespace `
      --name library-indexing --max-delivery-count 5 --lock-duration PT5M
  # ...and on the containerapp, a second --scale-rule-name library-depth of type azure-servicebus
  # with queueName=library-indexing (az containerapp update supports repeated scale rules;
  # or express both rules in a YAML/Bicep spec, which is cleaner than stacked CLI flags).
  ```
  Add the same queue block to `scripts/servicebus-emulator.json`. Independently, set
  `services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = Ignore)` or wrap
  `StartProcessingAsync` so one dead queue can't kill the other worker.
- **Effort:** small
- **Risk:** Adding a scale rule changes wake behaviour; verify the KEDA auth (`sb-manage`) has
  Manage rights on the new queue too — the script only grants it on `source-extraction`.

### [SEVERITY: High] Worker startup hard-fails on `BlobStorage:ConnectionString`, which provisioning never sets
- **Where:** `src/Nornis.Worker/Program.cs:156-159` vs `scripts/provision-azure.ps1:127-141`;
  `src/Nornis.Worker/appsettings.json` (no `BlobStorage` section).
- **What:** `Program.cs` throws `InvalidOperationException` at startup if
  `BlobStorage:ConnectionString` is empty. The worker container app is created with exactly four
  env vars — `ConnectionStrings__DefaultConnection`, `ServiceBus__ConnectionString`,
  `Extraction__AiApiKey`, `Extraction__AiEndpoint`. No `BlobStorage__ConnectionString`, no
  `Library__*`, no `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- **Why it costs:** A worker provisioned from this script crash-loops on boot. On a scale-to-zero
  app that means every queue message triggers a replica start, an ACR pull, a fail, and a backoff
  — repeated container starts billed for zero completed work, and every extraction message ages
  to the dead-letter queue. The live deployment has evidently been patched by hand; that means
  the script no longer reproduces prod, so the next `provision-azure.ps1` run is a landmine.
- **Fix:** Add the secret + env var to the worker app creation (mirroring how `extract-key` is
  handled), and reconcile the script against whatever the live app actually has
  (`az containerapp show -n ca-nornis-worker --query properties.template.containers[0].env`).
  Also worth making blob storage lazily-failing rather than startup-fatal, so a missing library
  config degrades indexing instead of killing extraction.
- **Effort:** trivial (script), small (reconcile)
- **Risk:** None beyond re-running provisioning, which is already `Stop`-on-error and idempotent.

### [SEVERITY: High] A 200 MB PDF is buffered whole into a 0.5 GiB worker
- **Where:** `src/Nornis.Infrastructure/Storage/PdfPigTextExtractor.cs:10-24`,
  `src/Nornis.Application/Services/LibraryIndexingService.cs:80-118`,
  `src/Nornis.Application/Configuration/LibraryOptions.cs` (`MaxUploadSizeBytes = 209_715_200`),
  `scripts/provision-azure.ps1:131` (`--cpu 0.25 --memory 0.5Gi`).
- **What:** The indexing pipeline is fully in-memory and fully materialized at each stage: the
  blob stream is `CopyToAsync`'d into a `MemoryStream` (PdfPig needs seekable), then *every*
  page's text into a `List<PdfPageText>`, then every chunk, then every 1536-float embedding into
  a `List<LibraryChunkWrite>` — none of it released until the single `ReplaceForDocumentAsync` at
  the end. Nothing enforces a smaller limit worker-side.
- **Why it costs:** `MemoryStream` doubles its buffer as it grows, so a 200 MB blob peaks near
  400 MB of managed heap (much of it LOH) before a single page is parsed — on a container with
  512 MiB total. The container is OOM-killed, the message is never completed, Service Bus
  redelivers, and the next attempt OOMs at the same place. Every attempt re-pays for any
  embeddings already bought before the kill, and you pay container-seconds for `MaxDeliveryCount`
  doomed attempts before the message dead-letters. A moderately large sourcebook (say 60 MB) will
  survive extraction but leave almost no headroom for the embedding accumulation.
- **Fix:** Two independent changes, either helps: (a) raise worker memory to 2–4 GiB
  (`--cpu 1.0 --memory 2.0Gi`) — cheap, since the app is scale-to-zero and only pays while
  working; (b) stop accumulating: write chunk batches to the repository as each embedding batch
  returns, instead of building the whole `writes` list. Also cap `MaxUploadSizeBytes` to something
  the worker can actually survive, and stream the blob to a temp file rather than a `MemoryStream`
  (`await using var tmp = new FileStream(Path.GetTempFileName(), ..., FileOptions.DeleteOnClose)`).
- **Effort:** trivial (a) / medium (b)
- **Risk:** (b) changes `ReplaceForDocumentAsync`'s all-or-nothing semantics — a partial write
  must be visible as partial, which needs the resume logic from the next finding.

### [SEVERITY: High] Library indexing has no checkpoint — every failure re-buys every embedding
- **Where:** `src/Nornis.Application/Services/LibraryIndexingService.cs:92-118`
  (accumulate loop) and `:118` (single terminal `ReplaceForDocumentAsync`).
- **What:** Embeddings for all chunks are computed into an in-memory list; the only persistence
  happens after the *last* batch succeeds. A transient failure on batch 40 of 41 (`:129`) returns
  `Transient`, the worker abandons the message (`LibraryIndexingWorker.cs:92`), and the redelivered
  message re-runs `ProcessIndexingAsync` from the top — re-downloading the blob, re-parsing the PDF,
  and re-embedding chunks 1..39 that were already paid for.
- **Why it costs:** Directly in Azure OpenAI embedding tokens. At the configured
  `nornis-embed` rate ($0.02/M input) a 500-page book is small money per pass, but the failure
  mode is a *loop*: with no backoff (next finding) and `MaxDeliveryCount` redeliveries, one
  sustained 429 window means N full re-embeddings of the entire document plus N full container
  runs. It also burns the world's daily budget guard, which then blocks the GM's actual extraction
  work for the rest of the day.
- **Fix:** Persist per batch and resume. Add a "highest `Ord` already stored for this document"
  read at entry and skip chunks below it:
  ```csharp
  var resumeFrom = await _chunkRepository.GetMaxOrdAsync(document.Id, ct); // -1 if none
  foreach (var batch in chunks.Where(c => c.Ord > resumeFrom).Chunk(_options.EmbedBatchSize))
  {
      var result = await _embeddingClient.EmbedAsync(...);
      await _chunkRepository.AppendAsync(document.Id, BuildWrites(batch, result), ct);
  }
  ```
  Keep `ReplaceForDocumentAsync` for the genuine reindex path (clear once, at `Ord == 0`).
- **Effort:** medium
- **Risk:** Needs care that a *content change* reindex clears old chunks; key the resume on a
  document content hash / `UpdatedAt` so a re-upload doesn't resume onto stale vectors.

### [SEVERITY: High] Transient failures abandon with zero backoff — a throttle turns into a tight retry loop
- **Where:** `src/Nornis.Worker/ExtractionWorker.cs:175,194`;
  `src/Nornis.Worker/LibraryIndexingWorker.cs:92,106`;
  `src/Nornis.Application/Services/ExtractionService.cs:1702-1707` (status reset to `Queued`).
- **What:** `AbandonMessageAsync` makes the message immediately available again — no
  `ScheduleMessageAsync`, no delay, no delivery-count-aware wait. `TransientOutcomeAsync`
  deliberately puts the source back to `Queued` so the retry isn't a no-op, which is correct, but
  it means the redelivery re-runs the *whole* expensive path: `AssembleContextAsync` (multiple DB
  queries, artifact context up to `MaxArtifactContextCount: 50`, reference-passage retrieval) plus
  a fresh AI completion.
- **Why it costs:** The single most common transient failure is Azure OpenAI 429/throttling
  (`IsTransientException` matches exactly that). The response to a throttle is therefore an
  *immediate* re-request — the textbook way to extend a throttle window. Each cycle re-pays the
  context-assembly DB load and any tokens the retried call does consume, and the inner parse-retry
  loop (`MaxParseRetryAttempts: 2` → 3 completions per attempt at `ExtractionService.cs:1055`)
  multiplies it: worst case ~3 completions × `MaxDeliveryCount` redeliveries per source. The daily
  budget guard bounds the dollar damage but is only re-checked once per delivery, and once it
  trips it blocks legitimate work.
- **Fix:** Delay proportional to `args.Message.DeliveryCount` instead of a bare abandon:
  ```csharp
  var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, args.Message.DeliveryCount) * 5));
  await args.DeferOrScheduleAsync(...); // simplest: re-enqueue a copy with
                                        // ScheduledEnqueueTime = DateTimeOffset.UtcNow + delay,
                                        // then CompleteMessageAsync the original
  ```
  Scheduled re-enqueue is the Service Bus-native backoff; it also keeps the replica free (and
  lets it scale back to zero) instead of spinning. If you keep plain abandon, at minimum add a
  jittered in-handler delay before abandoning.
- **Effort:** small
- **Risk:** Re-enqueue resets `DeliveryCount`, so carry an attempt counter in
  `ApplicationProperties` and dead-letter explicitly past a threshold — otherwise you lose the
  DLQ backstop entirely.

### [SEVERITY: High] Lock renewal caps at 5 minutes; a long indexing run gets delivered twice
- **Where:** `src/Nornis.Worker/Configuration/WorkerOptions.cs:13`
  (`MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)`), applied to *both* processors in
  `src/Nornis.Worker/Program.cs:146,191`; `src/Nornis.Worker/appsettings.json` (`"00:05:00"`).
- **What:** The processor auto-renews the message lock for at most 5 minutes of handler runtime.
  Extraction is plausibly within that (`AiTimeoutSeconds: 60` × 3 parse attempts + context
  assembly = tight but usually fine). Library indexing is not: a book-sized PDF is dozens of
  serial embedding round trips plus PDF parsing.
- **Why it costs:** Past 5 minutes the lock expires while the handler is still running. Service Bus
  redelivers the message to the same or another consumer, which starts the *entire* indexing run
  again in parallel with the first — double-paying every embedding for the document. The first
  run's `CompleteMessageAsync` then fails (lock lost) and throws into the processor error handler,
  so the message stays in flight and can be delivered a third time. `document.Status != Indexing`
  is not a guard here, because the status stays `Indexing` for the whole run.
- **Fix:** Give the two processors different renewal budgets — extraction can stay at 5 minutes,
  indexing needs 30+ (`MaxAutoLockRenewalDuration` is a *cap*, not a fixed cost, so raising it is
  free when runs are short). Split `WorkerOptions` into per-queue settings, or add
  `LibraryMaxAutoLockRenewalDuration`. The checkpointing fix above also makes a duplicate
  delivery cheap rather than catastrophic.
- **Effort:** trivial
- **Risk:** A longer renewal window means a hung handler holds a message longer before it becomes
  redeliverable. Pair with a real per-run timeout.

### [SEVERITY: High] Throughput ceiling of exactly one message at a time, globally
- **Where:** `src/Nornis.Worker/appsettings.json` (`"MaxConcurrentCalls": 1`),
  `src/Nornis.Worker/Configuration/WorkerOptions.cs:9`, `scripts/provision-azure.ps1:130`
  (`--min-replicas 0 --max-replicas 1`).
- **What:** One replica, `MaxConcurrentCalls = 1` on each of the two processors. The workload is
  almost entirely *waiting* — HTTP round trips to Azure OpenAI (60 s timeout each), blob reads, SQL
  queries. The 0.25 vCPU sits idle for the overwhelming majority of the billed wall clock.
- **Why it costs:** Container Apps bills vCPU-seconds and GiB-seconds for the whole time a replica
  is up, not for CPU actually used. A backlog of N sources costs N × (AI latency) replica-seconds
  instead of N/C. For the documented Symbaroum-scale import (83 sources) that is over an hour of
  billed replica time doing nothing but blocking on HTTPS. It also means a library indexing job
  and an extraction can never overlap, and one slow PDF stalls every GM's extraction queue.
- **Fix:** Raise `MaxConcurrentCalls` to 4–8 (the DI scope is already per-message, so this is
  safe — `ExtractionWorker.cs:98-100` calls it out explicitly) and set `--max-replicas 3`. Note
  `scripts/import-notes.py` intentionally serializes *its* work at the client, so imports are
  unaffected; the win is concurrent worlds, library indexing, and backfill sweeps. Also raise
  `PrefetchCount` above 0 (say `2 × MaxConcurrentCalls`) so the receiver isn't doing a round trip
  per message.
- **Effort:** trivial (config) + small (verify no shared-state assumptions in the services)
- **Risk:** Real. Concurrency multiplies pressure on the Azure OpenAI deployment's TPM quota —
  more 429s, which the no-backoff retry loop above handles badly. **Fix the backoff first.** Also
  raise memory before raising concurrency, since each in-flight indexing job holds a full PDF.

### [SEVERITY: High] Every API test builds its own ASP.NET host; nothing runs in parallel
- **Where:** `tests/Nornis.Api.Tests/**` — 42 `new NornisWebApplicationFactory()` call sites, 50
  `[SetUp]` fixtures, **0** `[OneTimeSetUp]` anywhere in `tests/`; e.g.
  `tests/Nornis.Api.Tests/Artifacts/ArtifactsControllerTests.cs:16`,
  `tests/Nornis.Api.Tests/Controllers/DemoWorldTests.cs:27`.
  No `[assembly: Parallelizable]`, no `.runsettings`, no `Directory.Build.targets`.
- **What:** `[SetUp]` (per *test method*, not per fixture) constructs a fresh
  `WebApplicationFactory<Program>` — which builds the full API host: configuration, the entire DI
  graph, the ~15 service-descriptor removals and re-registrations in
  `NornisWebApplicationFactory.ConfigureWebHost`, routing, auth handlers, a new in-memory EF
  database. `Nornis.Api.Tests` alone has 429 `[Test]`/`[TestCase]` attributes; the repo has 2270.
  Teardown/dispose hygiene is fine (46 `[TearDown]` with `Dispose`) — the cost is construction,
  not leakage. And because NUnit's default is `ParallelScope.None`, all 2270 run one after
  another within their assemblies.
- **Why it costs:** Host construction is the dominant per-test cost in this suite — hundreds of
  milliseconds each, times ~400 tests, serially. This is paid on every local `dotnet test`, in
  `ci.yml` on every PR, and again in `deploy.yml` on every push to main.
- **Fix:** Two independent wins.
  1. Move factory construction to `[OneTimeSetUp]` where the fixture's tests don't mutate shared
     state. The per-instance `_databaseName = Guid.NewGuid()` means isolation currently comes from
     the factory *instance*; to share a factory per fixture you need per-test DB reset instead
     (clear the in-memory store in `[SetUp]`), which is far cheaper than a host rebuild.
  2. Add `[assembly: Parallelizable(ParallelScope.Fixtures)]` in an `AssemblyInfo.cs` per test
     project. Fixture-level parallelism is safe here precisely because each fixture owns its own
     factory and database. Combine with `<TestTimeout>`-free defaults and NUnit's
     `LevelOfParallelism`.
- **Effort:** medium (mechanical but 50 fixtures)
- **Risk:** Any fixture with cross-test order dependence will surface. Do it project by project;
  `Nornis.Application.Tests` / `Nornis.Domain.Tests` (pure, mocked) can take the
  `Parallelizable` attribute immediately with near-zero risk.

---

## Medium

### [SEVERITY: Medium] `dotnet format` in CI silently rewrites files and always passes
- **Where:** `.github/workflows/ci.yml` — final step, `run: dotnet format`.
- **What:** Bare `dotnet format` *applies* fixes to the checked-out working tree and exits 0. On a
  CI runner the changes are then discarded. The step can never fail.
- **Why it costs:** ~30–60 s of runner time on every PR for a check that verifies nothing, plus
  false confidence — formatting drift lands on main unchallenged and gets fixed later in
  unrelated diffs.
- **Fix:** `run: dotnet format --verify-no-changes` (add `--no-restore` since restore already ran).
- **Effort:** trivial
- **Risk:** The first run will fail loudly if the repo has drifted; run `dotnet format` locally and
  commit before flipping the flag.

### [SEVERITY: Medium] Three `docker buildx build` invocations each export a full `mode=max` cache
- **Where:** `.github/workflows/deploy.yml`, "Build and push images" — `for svc in api web worker`
  loop, each with `--cache-from type=gha --cache-to type=gha,mode=max`.
- **What:** The Dockerfile is correctly structured as one shared `build` stage feeding three thin
  final stages, but the workflow invokes buildx three separate times against it. Each invocation
  re-imports the GHA cache and, on completion, **re-exports every layer** of the shared SDK build
  stage with `mode=max`.
- **Why it costs:** Cache export of a full .NET SDK build stage is hundreds of MB; doing it three
  times per deploy is minutes of upload plus churn against the repo's 10 GB GHA cache quota —
  which, once exceeded, evicts the very layers you were caching and makes the *next* deploy do a
  cold restore.
- **Fix:** One invocation for all three targets via `docker buildx bake` with a
  `docker-bake.hcl` declaring the three targets and a shared cache entry; bake builds the common
  stage once and exports the cache once. Failing that, keep `mode=max` only on the first
  (`api`) invocation and use `--cache-to type=gha,mode=min` for the other two.
- **Effort:** small
- **Risk:** Bake changes the build invocation shape; verify the three `--build-arg` values and
  both tags per image survive the translation.

### [SEVERITY: Medium] No NuGet package caching in either workflow
- **Where:** `.github/workflows/ci.yml` (`actions/setup-dotnet@v4` then `dotnet restore`);
  `.github/workflows/deploy.yml` `test` job (same). No `nuget.config`, no `packages.lock.json`,
  no `actions/cache` step anywhere.
- **What:** Every CI run and every deploy test run downloads the full package graph from nuget.org
  — EF Core, Azure SDK, MudBlazor, ImageSharp, PdfPig, the NUnit/FsCheck/NSubstitute stack.
- **Why it costs:** Tens of seconds to a couple of minutes of runner time per run, on every PR
  push and every merge.
- **Fix:** Add `actions/cache` on `~/.nuget/packages` keyed on a hash of the `.csproj` files, or
  generate `packages.lock.json` (`<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`
  in `Directory.Build.props`) and use `setup-dotnet`'s built-in `cache: true`. The lock file also
  makes restores deterministic and is a prerequisite for reproducible Docker restore layers.
- **Effort:** trivial (actions/cache) / small (lock files)
- **Risk:** Lock files need regenerating on every dependency bump; CI will fail loudly if stale,
  which is the point.

### [SEVERITY: Medium] The solution is fully compiled twice on every push to main
- **Where:** `.github/workflows/deploy.yml` — the `test` job runs
  `dotnet restore/build/test Nornis.sln`, while the `build` job's Dockerfile independently runs
  `dotnet restore` + three `dotnet publish` over the same sources.
- **What:** Two full compiles of the same commit, on two runners, with no artifact sharing.
- **Why it costs:** Roughly doubles the compute per deploy. It's deliberate (the comment says the
  jobs run in parallel so tests don't gate image build), so wall-clock is fine — but it's paid
  minutes, and it means a compile error is discovered twice.
- **Fix:** Acceptable as-is if wall-clock is the priority; the honest cheaper option is a single
  job that builds once, runs tests from that output, and hands the publish directories to
  `docker buildx` via a runtime-only Dockerfile stage. Only worth it if runner minutes are a real
  constraint.
- **Effort:** medium
- **Risk:** Loses the "images exist even for a failing commit" property the current comment calls
  out as intentional.

### [SEVERITY: Medium] Transient-vs-permanent is decided by substring-matching exception messages
- **Where:** `src/Nornis.Application/Services/ExtractionService.cs:1681-1685` and the near-identical
  `src/Nornis.Application/Services/LibraryIndexingService.cs:181-187`.
- **What:** `ex.Message.Contains("429")`, `Contains("503")`, `Contains("rate limit")`,
  `Contains("service unavailable")`. Duplicated in two services with slightly different sets
  (indexing also treats `TimeoutException` as transient; extraction doesn't).
- **Why it costs:** Both directions cost money. A genuine throttle whose message doesn't contain
  those literals (SDK wording changes, a wrapped `RequestFailedException`, a localized message)
  is classified non-transient — the document is marked `IndexFailed` / the source `Failed`, and
  everything already spent on that run is written off, requiring a manual re-run that pays again.
  Conversely a non-retryable error whose message happens to contain "429" gets abandoned into
  the retry loop. The extraction path already has the right tool one method away
  (`IsPermanentHttpFailure` at `:1691` correctly reads `ex.StatusCode`).
- **Fix:** Classify on typed data — `RequestFailedException.Status`,
  `HttpRequestException.StatusCode`, `ClientResultException.Status` — and hoist one shared
  `TransientFailureClassifier` into `Nornis.Application` so both services agree.
- **Effort:** small
- **Risk:** Reclassification changes which failures retry; check the existing tests that assert
  outcome types before changing the boundary.

### [SEVERITY: Medium] Embedding batches are issued strictly serially
- **Where:** `src/Nornis.Application/Services/LibraryIndexingService.cs:92-116` —
  `foreach (var batch in chunks.Chunk(_options.EmbedBatchSize))` with an `await` inside.
- **What:** Each embedding round trip completes before the next starts. With
  `EmbedBatchSize = 64` and `MaxChunkChars = 3200`, a 500-page book is on the order of 8–10
  sequential calls; a large sourcebook, many more.
- **Why it costs:** Pure billed wall clock on the container while the CPU idles — the same
  problem as `MaxConcurrentCalls = 1` but inside a single job. It's also what pushes the run past
  the 5-minute lock-renewal cap above.
- **Fix:** Bounded parallelism over the batches, e.g. `Parallel.ForEachAsync` with
  `MaxDegreeOfParallelism = 4`, writing results into a pre-sized array indexed by batch ordinal so
  chunk order is preserved. Keep it bounded — unbounded fan-out onto the embedding deployment is
  a self-inflicted 429.
- **Effort:** small
- **Risk:** Interacts with the checkpointing fix (out-of-order completion complicates "resume from
  max Ord"); decide the checkpoint scheme first.

### [SEVERITY: Medium] Nothing gives in-flight work a shutdown budget, so a deploy discards it
- **Where:** No `HostOptions.ShutdownTimeout`, no `BackgroundServiceExceptionBehavior`, no
  `terminationGracePeriodSeconds` anywhere in `src/`, `scripts/`, or `.github/` (verified by
  grep). `src/Nornis.Worker/Program.cs:199-200` is a bare `Build()`/`Run()`.
  `.github/workflows/deploy.yml` runs `az containerapp update` on all three apps in parallel.
- **What:** On deploy, Container Apps starts draining the old worker revision.
  `ProcessMessageEventArgs.CancellationToken` is signalled, the in-flight AI call is cancelled,
  and the handler's `catch (Exception)` then calls
  `AbandonMessageAsync(..., cancellationToken: args.CancellationToken)` — with a token that is
  *already cancelled* (`ExtractionWorker.cs:194`, `LibraryIndexingWorker.cs:106`). The abandon
  itself throws; the message just sits locked until the lock expires.
- **Why it costs:** Every deploy that lands mid-extraction throws away that extraction's AI spend
  and delays the message by a full lock duration before redelivery. Partially mitigated — derived
  text and handwriting transcription *are* persisted before the main call
  (`ExtractionService.cs:196,216`), so those aren't re-bought — but the main completion is. Library
  indexing loses everything (no checkpoint).
- **Fix:** Pass `CancellationToken.None` to the abandon calls so the message is released promptly
  rather than left locked. Then give the host a real drain window:
  `services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMinutes(2));` and set a
  matching `terminationGracePeriodSeconds` on the container app. The `StopProcessingAsync` call
  already passes `CancellationToken.None` (`ExtractionWorker.cs:52`), so the plumbing is half
  there.
- **Effort:** small
- **Risk:** A longer grace period slows rollouts; two minutes is fine for a scale-to-zero worker.

### [SEVERITY: Medium] No central package management — 7 projects hand-maintaining versions
- **Where:** No `Directory.Packages.props` (verified absent). `Microsoft.NET.Test.Sdk 17.11.1`,
  `NUnit 4.2.2`, `NUnit3TestAdapter 4.6.0` are each repeated across all 7 test projects;
  `Azure.AI.OpenAI 2.1.0` in both `Nornis.Worker.csproj` and `Nornis.Infrastructure.csproj`;
  `Azure.Messaging.ServiceBus 7.20.1` in `Nornis.Api.csproj` and `Nornis.Infrastructure.csproj`;
  `Azure.Monitor.OpenTelemetry.AspNetCore 1.5.0` in three projects. (MSBuild anti-pattern AP-09.)
- **What:** Versions are currently *consistent* — this is a drift-risk finding, not an active bug.
  But the repo also carries two security-driven transitive pins with explanatory comments
  (`SQLitePCLRaw.bundle_e_sqlite3 3.0.3` in `Nornis.Infrastructure.Tests.csproj`, `AngleSharp 1.5.2`
  in `Nornis.Web.Tests.csproj`) — exactly the class of pin that CPM exists to make repo-wide.
- **Why it costs:** Not runtime cost; maintenance cost and a future diamond-dependency debugging
  session. With `TreatWarningsAsErrors` on and NU1902/NU1903 promoted to errors, a version that
  drifts in one project breaks the build in a way that's tedious to trace.
- **Fix:** Add `Directory.Packages.props` with `<ManagePackageVersionsCentrally>true</…>` and
  `<PackageVersion>` entries; strip `Version=` from every `PackageReference`. Put the two security
  pins there with their comments, and add
  `<CentralPackageTransitivePinningEnabled>true</…>` so they apply transitively rather than needing
  a fake direct reference.
- **Effort:** small
- **Risk:** Mechanical; a bad transcription surfaces immediately at restore.

---

## Low

### [SEVERITY: Low] `Nornis.Worker` references `Nornis.Shared` but no worker source uses it
- **Where:** `src/Nornis.Worker/Nornis.Worker.csproj` — `<ProjectReference Include="..\Nornis.Shared\…" />`.
  Confirmed by grep: zero `Nornis.Shared` references under `src/Nornis.Worker/*.cs`.
  Same shape in `src/Nornis.Api/Nornis.Api.csproj`, which references `Application`, `Infrastructure`
  *and* `Shared` when `Infrastructure` already brings both transitively.
- **What:** Redundant edges in the project graph.
- **Why it costs:** Negligible — the transitive edges exist regardless, so the graph's critical
  path is unchanged. Purely tidiness.
- **Fix:** Drop the redundant `<ProjectReference>` lines.
- **Effort:** trivial
- **Risk:** If any file *does* rely on the direct reference for a `using`, the build fails
  immediately — safe to try.

### [SEVERITY: Low] A test project references another test project, serializing the test build graph
- **Where:** `tests/Nornis.Infrastructure.Tests/Nornis.Infrastructure.Tests.csproj` —
  `<ProjectReference Include="..\Nornis.Application.Tests\Nornis.Application.Tests.csproj" />`,
  for `Nornis.Application.Tests.Fakes` (5 files use it).
- **What:** `Nornis.Infrastructure.Tests` cannot start compiling until `Nornis.Application.Tests`
  finishes, and `Nornis.Application.Tests` itself references `src/Nornis.Infrastructure` (only 4
  of its files need it). The two largest non-API test projects are thus forced into a chain
  instead of building side by side.
- **Why it costs:** Lengthens the critical path of a parallel `dotnet build`, on every CI run and
  every local build.
- **Fix:** Extract the shared fakes into a small `tests/Nornis.TestFakes` library that both test
  projects reference. That also removes the reason `Nornis.Application.Tests` reaches into
  `Nornis.Infrastructure`.
- **Effort:** small
- **Risk:** Namespace churn across the ~9 affected files.

### [SEVERITY: Low] `PrefetchCount = 0` means an AMQP round trip per message
- **Where:** `src/Nornis.Worker/Configuration/WorkerOptions.cs:11`,
  `src/Nornis.Worker/appsettings.json` (`"PrefetchCount": 0`).
- **What:** No prefetch; the receiver fetches one message at a time.
- **Why it costs:** Adds a round trip of latency per message and a billed Service Bus operation
  per fetch. Immaterial at current volume, meaningful during a bulk import.
- **Fix:** Set prefetch to roughly `2 × MaxConcurrentCalls` once concurrency is raised.
- **Effort:** trivial
- **Risk:** Prefetched messages start their lock clock on fetch, so an over-large prefetch with
  slow handlers causes lock expiry. Keep the multiple small.

### [SEVERITY: Low] API and Web are pinned to exactly one always-on replica
- **Where:** `scripts/provision-azure.ps1:100,116` — both
  `--min-replicas 1 --max-replicas 1 --cpu 0.25 --memory 0.5Gi`.
- **What:** No scaling in either direction. The Web app additionally has sticky sessions set for
  the Blazor Server circuit, which is why `max-replicas 1` is defensible there.
- **Why it costs:** Two always-on 0.25 vCPU / 0.5 GiB replicas is the fixed floor of the hosting
  bill. It's also a single point of failure: any deploy or platform restart is a hard outage, and
  a traffic spike has nowhere to go.
- **Fix:** Out of scope to change blindly (min-replicas 0 on the API means cold starts on the
  Web app's HTTP calls, and the Blazor circuit genuinely wants affinity). Worth *measuring* before
  changing. If you raise `--max-replicas` on Web, the sticky-session config already supports it.
- **Effort:** small
- **Risk:** Cold starts; Blazor circuit affinity.

### [SEVERITY: Low] `Directory.Build.props` sets `TargetFramework` unconditionally
- **Where:** `Directory.Build.props:3-4`.
- **What:** `<TargetFramework>net10.0</TargetFramework>` with no `Condition`. (MSBuild
  anti-pattern AP-15 — unconditional property set in an outer scope.) Note the worker's `obj/`
  and `bin/` still contain stale `net8.0` output directories from an earlier retarget.
- **Why it costs:** Any project that later needs multi-targeting must fight the root props rather
  than opt in; and a `TargetFrameworks` (plural) in a csproj would silently conflict.
- **Fix:** `<TargetFramework Condition="'$(TargetFramework)' == '' and '$(TargetFrameworks)' == ''">net10.0</TargetFramework>`.
  Separately, `git clean -xfd` the stale `net8.0` output dirs.
- **Effort:** trivial
- **Risk:** None.

### [SEVERITY: Low] Two csproj files have a mangled opening line and a UTF-8 BOM
- **Where:** `src/Nornis.Api/Nornis.Api.csproj:1` and `src/Nornis.Worker/Nornis.Worker.csproj:1` —
  both begin `﻿<Project Sdk="…"><PropertyGroup>  <UserSecretsId>…` on a single line.
- **What:** Cosmetic damage from an earlier automated edit. Valid XML; builds fine.
- **Why it costs:** Nothing at build time. It makes those two files harder to diff and review than
  the other five.
- **Fix:** Reformat to the normal layout used by the other projects.
- **Effort:** trivial
- **Risk:** None.

### [SEVERITY: Low] `import-notes.py` polls the review queue every 6 seconds for up to 10 minutes per source
- **Where:** `scripts/import-notes.py` — `POLL_SECONDS = 6`, `EXTRACT_TIMEOUT = 600`, and the
  `while True:` poll loops around line 218 and 236.
- **What:** After enqueuing each source the script polls the API until proposals appear, then
  batch-accepts before sending the next. Up to 100 API round trips per source.
- **Why it costs:** Trivial in absolute terms (it's an operator-run one-off), but it holds the
  API busy for the entire import and the strict serialization means an 83-source import is
  bounded by the sum of extraction latencies. The docstring already documents the real cost
  driver: ~$5.50 and a day's budget for one vault.
- **Fix:** Leave it. The serialization is deliberate and load-bearing for extraction quality (the
  docstring explains why). If it ever matters, widen `POLL_SECONDS` with backoff rather than
  parallelizing.
- **Effort:** trivial
- **Risk:** None.

---

## Unverified / worth a look

These could not be confirmed from the repo and need a live check:

1. **Live Container App config vs. `provision-azure.ps1`.** The script cannot produce a working
   worker (missing `BlobStorage__ConnectionString`), so prod has diverged. The real
   `--min-replicas`, `--max-replicas`, `--memory`, env vars, and scale rules should be read back
   with `az containerapp show -g rg-nornis -n ca-nornis-worker` before acting on any finding that
   cites the script's values.
2. **Prod `MaxDeliveryCount` and `LockDuration` on `source-extraction`.** The emulator config
   uses 3 and `PT1M`; the Azure defaults are 10 and `PT1M`. The severity of the no-backoff retry
   loop scales directly with this number, and the 5-minute renewal cap only helps if the queue's
   own lock duration is sane.
3. **Whether the `library-indexing` queue exists in prod at all.** If it does not, the worker
   host is crash-looping (or the second hosted service is silently dead) — check worker logs for
   `MessagingEntityNotFound`.
4. **Actual `dotnet test` wall clock.** I did not run the suite (dev servers hold locks on
   `Nornis.Api.Tests` / `Nornis.Web.Tests` binaries). The per-test-host claim is a structural read
   of 42 construction sites and 0 `OneTimeSetUp`; the size of the win should be measured with
   `dotnet test tests/Nornis.Api.Tests --logger "console;verbosity=normal"` before spending the
   medium effort.
5. **`ProcessMessageEventArgs.CancellationToken` cancellation semantics on drain.** I reasoned
   from the Azure SDK's documented behaviour (the token is signalled when the processor stops)
   rather than from a runtime trace. The "abandon with an already-cancelled token" claim in the
   graceful-shutdown finding depends on it — trivially verifiable by logging the token state in
   the catch block during a rollout.
6. **`APPLICATIONINSIGHTS_CONNECTION_STRING` on the worker.** `Program.cs:30` gates all OTel on
   it and `provision-azure.ps1` never sets it, so the worker may be emitting no telemetry at all
   — which would explain why none of the above has been visible. Not a cost finding per se, but
   it's why cost findings go unnoticed.
