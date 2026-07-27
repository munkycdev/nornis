# Azure infrastructure-adapter cost audit — Nornis

Scope: `src/Nornis.Infrastructure/{Storage,Messaging,Notifications,Telemetry,Knowledge}`,
`src/Nornis.Application/{Storage,Messaging,Notifications,Knowledge}`, plus the DI registrations
in `src/Nornis.Api/Program.cs`, `src/Nornis.Web/Program.cs`, `src/Nornis.Worker/Program.cs`
and `scripts/provision-azure.ps1`. Persistence/Migrations and the AI/embedding side excluded.

Static source analysis only. No `az` commands were run; nothing was edited.

**Findings: 3 High, 8 Medium, 5 Low.**

---

## High

### [SEVERITY: High] Application Insights runs at 100% sampling in all three apps
- **Where:** `src/Nornis.Api/Program.cs:34-40`, `src/Nornis.Web/Program.cs:14-19`, `src/Nornis.Worker/Program.cs:30-36`
- **What:** All three call `.UseAzureMonitor()` with no options callback. `AzureMonitorOptions.SamplingRatio`
  defaults to `1.0F` and `TracesPerSecond` is unset, so the distro's fixed-rate sampler keeps everything.
  There is no `SamplingRatio`, `TracesPerSecond`, or `EnableTraceBasedLogsSampler` anywhere in the repo — I
  grepped the whole tree for `Sampling`, and the only hit is a comment in
  `src/Nornis.Infrastructure/Telemetry/AiUsageMetrics.cs:11` that says sampling might be "turned on later".
- **Why it costs:** the distro auto-instruments ASP.NET Core requests, `HttpClient`, `SqlClient`, and every
  `Azure.*` SDK. At 100% that means one ingested record for *every* HTTP request (including static files on
  `nornis-web`), *every* EF Core SQL round trip, *every* blob range GET, and *every* Service Bus receive —
  plus one `traces` record per `ILogger` call. App Insights bills ~$2.30/GB after the 5 GB/month free grant.
  Records average 1–2 KB. A single API request that runs 8 queries is ~10 records ≈ 15 KB ingested. This is
  the single largest telemetry lever in the repo and it is entirely unconfigured. Combined with finding #3
  (a 15-second unconditional UI poll), a handful of idle open browser tabs is enough to walk past the free
  grant on its own.
- **Fix:** set a ratio in all three `UseAzureMonitor()` calls, and keep the metrics pipeline unsampled
  (metrics already bypass the trace sampler, and `AiUsageMetrics` is deliberately low-cardinality — that
  part is correct):
  ```csharp
  .UseAzureMonitor(o =>
  {
      // 100% of errors is preserved by the distro's sampler for failed requests;
      // this trims the successful-request firehose.
      o.SamplingRatio = 0.10f;          // or o.TracesPerSecond = 2 for a rate cap
      o.EnableTraceBasedLogsSampler = true;
  })
  ```
  Separately, set a daily cap on `appi-nornis` (Usage and estimated costs → Daily cap). Nothing in this repo
  provisions the App Insights resource, so the cap has to be set out of band — see the Unverified section.
- **Effort:** trivial
- **Risk:** at 10% you lose per-request detail for successful requests; failed-request telemetry is retained
  by the distro's sampler, so incident debugging is largely unaffected. Any KQL that counts raw `requests`
  rows needs `* (1/samplingRate)` or `itemCount` applied — check the alert rules built on the OTel branch.

### [SEVERITY: High] Transient failures abandon the message immediately, re-billing the AI call up to MaxDeliveryCount times
- **Where:** `src/Nornis.Worker/ExtractionWorker.cs:175` and `:194`; `src/Nornis.Worker/LibraryIndexingWorker.cs:92` and `:106`; classification at `src/Nornis.Application/Services/ExtractionService.cs:1681-1685`
- **What:** on `OutcomeType.TransientFailure` (and on any unexpected exception) the worker calls
  `args.AbandonMessageAsync(...)` with no delay. Abandon releases the lock and makes the message
  *immediately* available again. `IsTransientException` classifies anything whose message contains `429`,
  `503`, `service unavailable`, or `rate limit` as transient, and timeouts route to
  `TransientOutcomeAsync` too (`ExtractionService.cs:334, 501, 810, 1093, 1099`).
- **Why it costs:** the exact condition that produces a 429 — Azure OpenAI quota saturated — is sustained,
  not momentary. Each redelivery re-runs the whole pipeline from the top: re-reads every attachment blob
  (see #5, several billed GETs each), re-runs the vision call, and re-issues the chat completion. A request
  that times out *after* the model has processed the prompt is billed for its input tokens; abandoning
  immediately buys that same bill again. With the deployed queue's MaxDeliveryCount (the local emulator
  config at `scripts/servicebus-emulator.json` uses 3; Azure's default is 10) one note can cost 3–10×
  its token budget before it dead-letters, and the whole cycle can complete in seconds because a 429 comes
  back fast. There is no backoff, no circuit breaker, and no jitter anywhere in the path.
- **Fix:** back off proportionally to `DeliveryCount`. The cheapest correct shape is to re-enqueue a
  scheduled copy and complete the original:
  ```csharp
  case OutcomeType.TransientFailure:
      var attempt = (int)args.Message.DeliveryCount;
      var delay = TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, attempt - 1)));
      // sender is the cached singleton sender from finding #8
      var retry = new ServiceBusMessage(args.Message)
      {
          ScheduledEnqueueTime = DateTimeOffset.UtcNow + delay
      };
      await sender.SendMessageAsync(retry, args.CancellationToken);
      await args.CompleteMessageAsync(args.Message, args.CancellationToken);
      break;
  ```
  Alternatively keep `AbandonMessageAsync` but add a `Task.Delay` before it — simpler, but it holds a
  concurrency slot and a lock renewal for the duration, which is worse at `MaxConcurrentCalls = 1`.
  Either way, cap total attempts in the message body so a permanently-throttled world stops retrying.
- **Effort:** small
- **Risk:** the scheduled-copy approach resets `DeliveryCount`, so the queue's MaxDeliveryCount no longer
  dead-letters it — you must carry an explicit attempt counter in `ExtractionMessage` and hard-fail past N.
  Getting that wrong turns a bounded retry into an unbounded one, which is strictly worse than today.
  Also check that `ExtractionService`'s idempotency guard (`TransientOutcomeAsync` resets the source to
  `Queued` at `:1705`) still lines up with a delayed redelivery.

### [SEVERITY: High] The nav bar polls the API every 15 seconds for every open circuit, unconditionally
- **Where:** `src/Nornis.Web/Components/Layout/NavMenu.razor:226` (also `TutorialChecklist.razor:137` @15s, `Sources.razor:178` @4s, `SourceDetail.razor:658` @4s, `Import.razor:664` @2s)
- **What:** `PollActivityAsync` runs a `PeriodicTimer(TimeSpan.FromSeconds(15))` for the lifetime of the
  Blazor Server circuit and calls `Api.GetSourceActivityAsync` on every tick. Unlike `Sources.razor:180`
  (which skips the fetch unless something is actually processing) and `TutorialChecklist.razor:141` (which
  checks `Visible && !AllDone`), NavMenu has no such guard — it fires whether or not there is any work.
- **Why it costs:** each tick is a Web→API HTTP dependency record, an API request record, and every SQL
  query the activity endpoint runs — all ingested at 100% (finding #1). 4 polls/min = 5,760/day per open
  tab. At a conservative ~6 telemetry items × 1.5 KB that is ~50 MB/day/tab, ~1.5 GB/month/tab — i.e. one
  person leaving Nornis open in a background tab consumes roughly a third of the entire free App Insights
  grant, doing nothing. It also keeps `ca-nornis-api` (min-replicas 1) permanently non-idle, so Container
  Apps never sees an idle window.
- **Fix:** give NavMenu the same guard the other pollers have, and slow the idle case down:
  ```csharp
  // Poll fast only while something is actually in flight; otherwise a slow heartbeat.
  var interval = _activity is { HasWorkInFlight: true } ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2);
  ```
  Better still, `ActivitySignal` already exists (`src/Nornis.Web/State/ActivitySignal.cs`, wired at
  `NavMenu.razor:205`) — driving the badge from that signal plus a slow safety poll removes most of the
  traffic. Also stop polling when the circuit is backgrounded (`Page Visibility` via JS interop).
- **Effort:** small
- **Risk:** badges update more slowly when a job finishes without raising the signal. Verify
  `ActivitySignal` fires on the worker-completion path before leaning on it, or the badge goes stale.

---

## Medium

### [SEVERITY: Medium] Every blob read and metadata fetch pays for an extra `ExistsAsync` round trip
- **Where:** `src/Nornis.Infrastructure/Storage/AzureBlobStorageService.cs:68` (in `GetBlobMetadataAsync`) and `:87` (in `OpenReadAsync`)
- **What:** `GetBlobMetadataAsync` calls `ExistsAsync` and then `GetPropertiesAsync` — two HEAD requests
  that return the same information. `OpenReadAsync` calls `ExistsAsync` purely to convert a 404 into a
  `FileNotFoundException` for callers (`ExtractionService.cs:771`, `LibraryIndexingService.cs:140`,
  `WorldExportService.cs:356`).
- **Why it costs:** `ExistsAsync` issues a real `Get Blob Properties` request; a 404 is still a billed
  transaction. Concretely, confirming one library upload (`LibraryService.cs:146`) costs **2 blob
  transactions where 1 would do** — 100% overhead on that call. A world export over 200 attachments
  (`WorldExportService.cs:354`) spends **200 avoidable HEAD requests**. At 100% telemetry sampling each of
  those is also an ingested dependency record, so the ingestion cost of the wasted call exceeds the storage
  cost of it.
- **Fix:** delete both `ExistsAsync` calls and let the 404 surface:
  ```csharp
  public async Task<BlobMetadata?> GetBlobMetadataAsync(string blobPath, CancellationToken ct = default)
  {
      try
      {
          var properties = await GetBlobClient(blobPath).GetPropertiesAsync(cancellationToken: ct);
          return new BlobMetadata(properties.Value.ContentLength, properties.Value.ContentType);
      }
      catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
      catch (RequestFailedException ex) { _logger.LogError(ex, "..."); return null; }
  }

  public async Task<Stream> OpenReadAsync(string blobPath, CancellationToken ct = default)
  {
      try { return await GetBlobClient(blobPath).OpenReadAsync(cancellationToken: ct); }
      catch (RequestFailedException ex) when (ex.Status == 404)
      { throw new FileNotFoundException($"Blob not found: {blobPath}"); }
  }
  ```
- **Effort:** trivial
- **Risk:** `OpenReadAsync` is lazy — with the `Exists` check gone, the 404 may not surface until the first
  `Read()` rather than at open time, depending on SDK version. The three call sites all catch
  `FileNotFoundException` around the *use* of the stream, not just the open, so check each one still catches
  it after the change. `GetBlobMetadataAsync` currently swallows all `RequestFailedException` into `null`,
  which means the "upload didn't arrive" error at `LibraryService.cs:149` is also what a 403 or a throttle
  looks like — worth splitting while you are in there.

### [SEVERITY: Medium] `OpenReadAsync` streams in small buffered chunks, and every caller buffers the whole blob anyway
- **Where:** `src/Nornis.Infrastructure/Storage/AzureBlobStorageService.cs:92`; callers at `ExtractionService.cs:301, 436, 731, 754, 764`, `LibraryIndexingService.cs:81`, `WorldExportService.cs:354`
- **What:** `blobClient.OpenReadAsync(cancellationToken: ct)` binds to the
  `(position, bufferSize, conditions, cancellationToken)` overload with `bufferSize` left null. The SDK's
  own parameter documentation for that overload says the buffer defaults to 1 MB (the
  `BlobOpenReadOptions.BufferSize` doc says 4 MB — the two disagree, see Unverified). Either way it is a
  *range GET per buffer*. And every consumer immediately copies the whole thing into memory anyway:
  `PdfPigTextExtractor.cs:13-16` copies to a `MemoryStream` because PdfPig needs seekability,
  `ExtractionService.cs:302-304` and `:765-767` copy to `MemoryStream` then `.ToArray()`, and
  `ExtractionService.cs:755-756` does `ReadToEndAsync`. The lazy streaming buys nothing.
- **Why it costs:** a 20 MB library PDF costs ~20 range GETs (at 1 MB) plus the `ExistsAsync` from the
  previous finding, versus 1–2 for a single `DownloadToAsync`. Multiply by the retry storm in finding #2 and
  one stuck document re-downloads itself 3–10 times. Each range GET is also an ingested dependency record
  at 100% sampling, so a single PDF index writes ~20 rows into App Insights.
- **Fix:** add a `DownloadToAsync(Stream, CancellationToken)` method to `IBlobStorageService` and use it
  where the caller is going to buffer regardless; the SDK's parallel download path issues far fewer, larger
  requests. Where `OpenReadAsync` genuinely must stay a stream (`WorldExportService.CopyBlobEntryAsync`
  streams straight into the zip), pass an explicit larger buffer:
  ```csharp
  return await blobClient.OpenReadAsync(
      new BlobOpenReadOptions(allowModifications: false) { BufferSize = 8 * 1024 * 1024 },
      cancellationToken);
  ```
- **Effort:** small
- **Risk:** a bigger buffer raises peak memory per concurrent operation — relevant because the worker runs
  at `--memory 0.5Gi` (`scripts/provision-azure.ps1:114`). Pair this with finding #10 before raising
  `MaxConcurrentCalls`, or a large PDF plus concurrency will OOM the container.

### [SEVERITY: Medium] A new `WebPushClient` — and a new undisposed `HttpClient` — per notification batch
- **Where:** `src/Nornis.Infrastructure/Notifications/WebPushNotificationSender.cs:82`
- **What:** `var client = new WebPushClient();` inside `NotifyUsersAsync`, used for the loop at `:84-87`
  and then dropped on the floor. I confirmed by reflecting over `WebPush` 1.0.13: `WebPush.WebPushClient`
  has private fields `_httpClient` (`System.Net.Http.HttpClient`), `_httpClientHandler`, and
  `_isHttpClientInternallyCreated`, implements `IDisposable`, and exposes a
  `WebPushClient(HttpClient)` constructor. The parameterless constructor is the one being used, so each
  call allocates its own `HttpClient` + handler, and nothing ever disposes it.
- **Why it costs:** this is the textbook socket-exhaustion shape. Every extraction finish that notifies
  someone creates a fresh connection pool to `fcm.googleapis.com` / `*.push.services.mozilla.com`, does a
  fresh TLS handshake per subscription, and leaves the sockets in `TIME_WAIT` until GC finalizes the
  handler. On the worker (`Program.cs:87-88`, registered scoped) this fires once per completed extraction;
  on a bulk import walk that is once per note. Under load the container runs out of ephemeral ports and
  notifications start failing — and because `SendOneAsync` swallows everything at `:124-131`, the failure
  is invisible except as a rising `LogWarning` count.
- **Fix:** register a typed/named client and hand it to the `WebPushClient(HttpClient)` overload.
  ```csharp
  // Program.cs (Api and Worker)
  services.AddHttpClient(nameof(WebPushNotificationSender));

  // WebPushNotificationSender — inject IHttpClientFactory, then:
  using var client = new WebPushClient(_httpClientFactory.CreateClient(nameof(WebPushNotificationSender)));
  ```
  Note `WebPushClient.Dispose()` will not dispose an externally-supplied `HttpClient`
  (`_isHttpClientInternallyCreated` guards exactly that), so the factory-owned handler lifetime is respected.
- **Effort:** small
- **Risk:** none functionally. Confirm `WebPushClient` does not mutate `DefaultRequestHeaders` on the shared
  client in a way that leaks between calls — if it does, use `AddHttpClient` with a per-call
  `HttpRequestMessage` instead, or keep one `WebPushClient` as a singleton.

### [SEVERITY: Medium] The KEDA scale rule only watches `source-extraction`; the `library-indexing` queue can never wake the worker
- **Where:** `scripts/provision-azure.ps1:110-122` (`--scale-rule-metadata "queueName=$Queue"` where `$Queue` defaults to `source-extraction` at `:23`), against the second processor registered at `src/Nornis.Worker/Program.cs:183-192`
- **What:** `ca-nornis-worker` runs at `--min-replicas 0` with exactly one scale rule, keyed on the
  `source-extraction` queue depth. `LibraryIndexingWorker` listens on `library-indexing`
  (`ServiceBusLibraryIndexingQueueClient.cs:9`) inside the same container, but nothing in the scale
  configuration observes that queue.
- **Why it costs:** a user uploads a library PDF, `LibraryService.ConfirmUploadAsync:166` enqueues an
  indexing message, and if no extraction is pending the worker stays at zero replicas — the document sits
  at `Indexing` indefinitely and the user sees a spinner that never resolves. When it eventually does run,
  it may be well past the queue's message TTL (the emulator config sets `PT1H`), in which case the message
  is silently dropped and the row is stuck forever. Cost-wise the failure mode is the expensive kind: the
  blob is stored and billed, the user re-uploads or hits reindex, and the same PDF gets embedded twice.
- **Fix:** add a second scale rule for the indexing queue:
  ```powershell
  az containerapp update -g $ResourceGroup -n ca-nornis-worker `
      --scale-rule-name library-queue-depth --scale-rule-type azure-servicebus `
      --scale-rule-metadata "queueName=library-indexing" "messageCount=1" `
      --scale-rule-auth "connection=sb-manage"
  ```
  The `keda-scaler` authorization rule at `:55-57` is created per-queue, so a matching rule is needed on
  `library-indexing` too. Also note `scripts/servicebus-emulator.json` only declares `source-extraction`,
  so local dev cannot exercise this path at all.
- **Effort:** trivial
- **Risk:** the worker wakes more often, which costs Container Apps compute — but that is the point. Confirm
  the deployed KEDA config actually matches this script before acting (see Unverified).

### [SEVERITY: Medium] A fresh `ServiceBusSender` (and AMQP link) is created and torn down for every single message
- **Where:** `src/Nornis.Infrastructure/Messaging/ServiceBusExtractionQueueClient.cs:23`, `src/Nornis.Infrastructure/Messaging/ServiceBusLibraryIndexingQueueClient.cs:23`
- **What:** `await using var sender = _serviceBusClient.CreateSender(QueueName);` inside the send method.
  The `ServiceBusClient` itself is correctly a singleton (`Api/Program.cs:226`,
  `Worker/Program.cs:123-127`) — that part is right — but the sender is not.
- **Why it costs:** each `CreateSender` + dispose opens and closes an AMQP link over the shared connection:
  extra round trips (link attach/detach) before every send, and extra latency inside the request that the
  user is waiting on. It is not a new TCP connection, so the cost is latency and churn rather than
  transactions — but it makes every enqueue measurably slower, and during a bulk import
  (`ImportSessionService` dispatches per note) it is one link cycle per note.
- **Fix:** cache the sender for the client's lifetime. The queue clients are already singletons
  (`Api/Program.cs:227-228`, `Worker/Program.cs:128`), so a readonly field is enough:
  ```csharp
  private readonly ServiceBusSender _sender;
  public ServiceBusExtractionQueueClient(ServiceBusClient client)
      => _sender = client.CreateSender(QueueName);
  // ... await _sender.SendMessageAsync(serviceBusMessage, ct);
  ```
- **Effort:** trivial
- **Risk:** `ServiceBusSender` is thread-safe, so concurrent sends are fine. The class should implement
  `IAsyncDisposable` so the link closes on shutdown; DI will call it for singletons. Check the existing
  tests in `tests/Nornis.*.Tests` that construct these clients — moving `CreateSender` into the constructor
  means a mock `ServiceBusClient` now needs to answer `CreateSender` at construction time.

### [SEVERITY: Medium] The worker opens three Service Bus connections where one would do
- **Where:** `src/Nornis.Worker/Program.cs:123-127` (standalone `ServiceBusClient` singleton), `:138-147` and `:183-192` (two `ServiceBusExtractionProcessor` instances), each of which builds its own client at `src/Nornis.Infrastructure/Messaging/ServiceBusExtractionProcessor.cs:26`
- **What:** `ServiceBusExtractionProcessor` takes a connection *string* and news up its own
  `ServiceBusClient` rather than accepting the injected one. The worker therefore holds three independent
  `ServiceBusClient` instances — and three AMQP connections — against the same namespace, all built from
  the same `options.ConnectionString`.
- **Why it costs:** connections are not billed directly, but they count against the namespace's concurrent
  connection limit (1,000 on Standard) and each costs a TLS handshake plus CBS token negotiation at
  startup. The worker runs at `--min-replicas 0` and cold-starts on every queue wake, so that handshake
  cost is paid constantly rather than once. It also triples the idle keepalive traffic.
- **Fix:** change the processor to accept an injected `ServiceBusClient` and not own its lifetime:
  ```csharp
  public ServiceBusExtractionProcessor(ServiceBusClient client, string queueName, int maxConcurrentCalls, ...)
  {
      _processor = client.CreateProcessor(queueName, options);   // no _client field, no client disposal
  }
  ```
  Both registrations already have the client available via `sp.GetRequiredService<ServiceBusClient>()`.
- **Effort:** small
- **Risk:** `DisposeAsync` at `ServiceBusExtractionProcessor.cs:76-80` currently disposes the client; after
  the change it must dispose only the processor, or the first worker to shut down kills the other's
  connection. Worth a `code-critic` pass since shutdown ordering across two hosted services is easy to get
  subtly wrong.

### [SEVERITY: Medium] Worker processes one message at a time with prefetch disabled
- **Where:** `src/Nornis.Worker/appsettings.json` (`"MaxConcurrentCalls": 1`, `"PrefetchCount": 0`), defaults mirrored at `src/Nornis.Worker/Configuration/WorkerOptions.cs:9-11`, applied at `src/Nornis.Worker/Program.cs:138-147` and `:183-192`
- **What:** both processors run with `MaxConcurrentCalls = 1` and `PrefetchCount = 0`.
- **Why it costs:** an extraction is dominated by a multi-second Azure OpenAI call during which the
  container is doing nothing but waiting on a socket. At concurrency 1 the Container App bills wall-clock
  vCPU-seconds for that idle wait, and a 90-note import walk serializes into 90 × (AI latency) of billed
  time. `PrefetchCount = 0` additionally means a separate receive round trip per message rather than one
  fetch amortized over several. Raising concurrency to 3 would cut the billed wall-clock of a bulk import
  by roughly two-thirds for the same token spend.
- **Fix:** `"MaxConcurrentCalls": 3, "PrefetchCount": 3` in `src/Nornis.Worker/appsettings.json` (values are
  already plumbed through options; no code change). Prefetch should not exceed what can be processed within
  `LockDuration` × renewal window.
- **Effort:** trivial (config) — but see Risk
- **Risk:** this is the one finding here I would *not* apply blind. Three concurrent extractions each buffer
  full attachment images into `byte[]` (`ExtractionService.cs:304`, `:767`) inside a `--memory 0.5Gi`
  container — a realistic OOM. It also triples the instantaneous rate against the Azure OpenAI deployment,
  which makes 429s *more* likely, which under finding #2 is actively expensive. Fix #2 first, then raise
  concurrency and memory together, and measure.

### [SEVERITY: Medium] No access tier or lifecycle policy on the blob container; world exports accumulate in Hot forever
- **Where:** `src/Nornis.Infrastructure/Storage/AzureBlobStorageService.cs:32-33` (container creation), `:95-102` (`UploadAsync` sets `ContentType` and nothing else), `src/Nornis.Application/Services/WorldExportService.cs:93-103`
- **What:** the container is created in code with `CreateIfNotExists(PublicAccessType.None)` and no
  lifecycle management rule exists anywhere in the repo. `UploadAsync` never sets `AccessTier`, so every
  blob lands in the account's default tier (Hot). Export zips are cleaned only by
  `DeleteByPrefixAsync` at the *start of the next export* (`WorldExportService.cs:93`) — a world exported
  once keeps its zip in Hot storage indefinitely.
- **Why it costs:** library PDFs and page scans are written once, read during indexing, and then read almost
  never; export zips are downloaded once and then never. Hot is ~$0.018/GB/month vs ~$0.010 Cool and
  ~$0.0036 Archive. The demo template alone ships a 3 MB map attachment
  (`src/Nornis.Api/DemoTemplate/vespergale-reach.zip`, 1 attachment entry, ~3.07 MB) that is re-uploaded to
  a fresh blob path for every demo world created (`DemoWorldService.cs:234-236`) — at
  `MaxCreationsPerUserPerDay: 1` that is 3 MB of duplicated Hot storage per demo world, forever.
- **Fix:** add a lifecycle management policy on the storage account: `worlds/*/exports/` → delete after
  7 days; `worlds/*/library/` and `worlds/*/sources/` → Cool after 30 days, Archive after 180. This is an
  account-level rule (portal or `az storage account management-policy create`), not a code change. If you
  prefer code, set `AccessTier = AccessTier.Cool` in the `BlobUploadOptions` at
  `AzureBlobStorageService.cs:100` for exports specifically.
- **Effort:** small
- **Risk:** Cool has a 30-day early-deletion charge and higher per-read cost — bad for anything that gets
  re-read often, so do not tier library PDFs down until reindex frequency is known. Archive requires an
  explicit rehydration (hours), which would break `GetDownloadAsync` outright; do not archive anything a SAS
  URL points at. Note this touches the *shared* `stchronicis` account — confirm the policy scope does not
  catch Chronicis's containers.

---

## Low

### [SEVERITY: Low] Blocking `CreateIfNotExists` inside the singleton constructor
- **Where:** `src/Nornis.Infrastructure/Storage/AzureBlobStorageService.cs:32-33`
- **What:** the constructor makes a synchronous network call to create the container. The factory that runs
  it (`Api/Program.cs:244-248`) is lazy, so this executes on whichever thread-pool thread first resolves
  `IBlobStorageService` — i.e. inside a user request.
- **Why it costs:** one billed container operation per process start (negligible), but it blocks a
  thread-pool thread on network I/O during a request, and it requires the connection string's credential to
  hold container-create rights it otherwise would not need. Under cold-start thundering-herd it is a
  sync-over-network stall on the request path.
- **Fix:** move container creation to deployment (it is a one-time provisioning step, and
  `scripts/provision-azure.ps1` is the natural home), or make it a lazily-awaited `Task` guarded by
  `SemaphoreSlim` if it must stay in-process.
- **Effort:** small
- **Risk:** if creation moves out of code, a fresh environment with no container fails at first upload
  instead of at startup — add it to the provisioning script in the same change.

### [SEVERITY: Low] `DeleteByPrefixAsync` issues one serial delete per blob
- **Where:** `src/Nornis.Infrastructure/Storage/AzureBlobStorageService.cs:110-117`; callers `WorldDeletionService.cs:61`, `WorldExportService.cs:93`
- **What:** `await foreach` over `GetBlobsAsync(prefix)` with a `DeleteBlobIfExistsAsync` per item, awaited
  one at a time.
- **Why it costs:** to be precise, this is a *latency* problem more than a billing one — Azure bills each
  subrequest of a Blob Batch individually, so batching does not reduce transaction count. But deleting a
  world with a few thousand blobs means a few thousand sequential round trips inside one HTTP request, and
  at 100% sampling, a few thousand ingested dependency records for a single delete.
- **Fix:** use `BlobBatchClient.DeleteBlobsAsync` (256 blobs per batch) from `Azure.Storage.Blobs.Batch`, or
  at minimum bound the parallelism with `Parallel.ForEachAsync(..., MaxDegreeOfParallelism = 8)`.
- **Effort:** small
- **Risk:** adds a package reference; batch delete is partial-failure-per-subrequest, so the error handling
  has to iterate responses rather than catch one exception. Do not raise parallelism without a cap — an
  unbounded fan-out against a throttled account will 503.

### [SEVERITY: Low] Per-request `LogInformation` that duplicates the request telemetry it rides on
- **Where:** `src/Nornis.Api/Controllers/CostsController.cs:38, 64, 89, 114, 204`
- **What:** five endpoints each log `"... requested. WorldId={WorldId}, UserId={UserId}, CorrelationId={...}"`
  at Information on every call, carrying `HttpContext.TraceIdentifier` as a custom dimension.
- **Why it costs:** the OTel request record already carries the operation name, the trace id, and (via
  baggage) the user. One cost-page load hits several of these endpoints, so each page view writes N extra
  `traces` rows that say nothing the `requests` rows do not. These are logs, not metrics, so the
  high-cardinality ids are not a metric-cardinality problem — just wasted GB. Across the codebase there are
  only 61 Information/Debug statements total, so application logging is otherwise disciplined; this
  controller is the outlier.
- **Fix:** drop these to `LogDebug`, or delete them and rely on request telemetry. If per-world attribution
  is wanted in App Insights, add `WorldId` as an activity tag once in middleware rather than as a log line
  per endpoint.
- **Effort:** trivial
- **Risk:** none. Check no alert rule or workbook queries these specific message strings first.

### [SEVERITY: Low] App Insights is not provisioned or capped by anything in the repo
- **Where:** `scripts/provision-azure.ps1` (creates Log Analytics at `:41-44` but never an Application Insights component); `.github/workflows/deploy.yml` (no `APPLICATIONINSIGHTS_CONNECTION_STRING` reference)
- **What:** all three apps activate Azure Monitor only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is
  present, but nothing in source control creates `appi-nornis`, sets its daily cap, or sets its retention.
- **Why it costs:** the single highest-leverage cost control for App Insights — the daily cap — lives
  entirely outside the repo, so it is invisible to review and easy to lose on a re-provision. Default
  retention is 90 days; data beyond 31 days is billed separately at ~$0.12/GB/month.
- **Fix:** add the component and its cap to `provision-azure.ps1` alongside the Log Analytics workspace:
  ```powershell
  az monitor app-insights component create -g $ResourceGroup -a appi-nornis `
      -l $Location --workspace $logId --retention-time 30 -o none
  az monitor app-insights component billing update -g $ResourceGroup -a appi-nornis `
      --cap 1 --stop-sending-mail-when-hit-cap false -o none
  ```
- **Effort:** small
- **Risk:** a daily cap *drops* telemetry once hit, including exception telemetry — set it high enough that
  a real incident is not blinded, and wire the cap-reached alert. Also note the container-apps environment
  logs already flow to the same Log Analytics workspace (`:47-48`), so container stdout and OTel logs are
  billed twice for overlapping content; worth checking before choosing a cap number.

### [SEVERITY: Low] The `ContinuityAudit` background loop runs on the API replica regardless of demand
- **Where:** `src/Nornis.Api/BackgroundServices/ContinuityAuditBackgroundService.cs:33-62`, registered at `src/Nornis.Api/Program.cs:176`
- **What:** a `while` loop that ticks every `TickIntervalHours`, queries every world with acceptances
  (`:76`), and issues two more queries per world (`:84-85`) before deciding eligibility.
- **Why it costs:** modest today, but it scales with world count rather than with work: N worlds means
  2N+1 queries per tick, every tick, forever, most of which conclude "not eligible". At 100% sampling those
  are all ingested dependency records. It also means `ca-nornis-api` can never be scaled to zero.
  `TickIntervalHours` has no value in `appsettings.json`, so it falls back to whatever
  `ContinuityAuditOptions` defaults to — worth pinning explicitly.
- **Fix:** push eligibility into a single query that returns only due worlds (one round trip per tick
  instead of 2N+1), and set `ContinuityAudit:TickIntervalHours` explicitly in configuration.
- **Effort:** small
- **Risk:** the eligibility rule (`ContinuityAuditEligibility.IsEligible`) is currently unit-testable in
  isolation; moving it into SQL loses that. Keep the C# predicate as the authority and use the query only
  to narrow candidates.

---

## Unverified / worth a look

These could not be confirmed from source and need a live check (I did not run `az`):

1. **Actual sampling and daily cap on `appi-nornis`.** The repo configures neither. If a cap or an
   ingestion-sampling setting was applied through the portal, finding #1's magnitude changes — but the code
   default of `SamplingRatio = 1.0F` is confirmed either way.
2. **`Azure.Storage.Blobs` 12.22.2 default read buffer.** The XML docs in the package contradict
   themselves: the `OpenReadAsync(position, bufferSize, ...)` parameter doc says "Defaults to 1 MB" while
   `BlobOpenReadOptions.BufferSize` says "Defaults to 4 MB". The code binds to the former overload. Finding
   #6 holds under either value; only the multiplier changes (20 vs 5 GETs for a 20 MB PDF).
3. **Deployed queue properties.** `MaxDeliveryCount`, `LockDuration`, `DefaultMessageTimeToLive`, and
   duplicate detection on the real `sb-nornis-dev` namespace. Only `scripts/servicebus-emulator.json` is in
   the repo (MaxDeliveryCount 3, TTL 1h, dup detection off) and that is the local emulator, not production.
   Finding #2's blast radius is 3× or 10× depending on this.
4. **Service Bus tier.** Standard's ~$10/month base includes 12.5M operations, which would make the
   *operation* cost of everything here effectively zero and leave only the AI-retry cost in finding #2.
   Premium is priced per messaging unit and behaves differently. The namespace lives in `rg-chronicis-dev`
   and is not created by this repo's script.
5. **Storage account default access tier and any existing lifecycle policy on `stchronicis`.** Finding #11
   assumes Hot with no policy because nothing in the repo sets one — but the account is shared with
   Chronicis and may already have rules.
6. **Whether the deployed KEDA configuration matches `provision-azure.ps1`.** The script is dated
   (its comment at `:80-82` about `ASPNETCORE_ENVIRONMENT=Development` and "auth is not built yet" is stale
   — Auth0 has since landed), so it may no longer reflect the live scale rules that finding #9 depends on.
7. **Whether `Azure.*` SDK ActivitySources are actually being exported.** The distro enables them by
   default, which is what makes every blob range GET and Service Bus receive an ingested record, but a
   view/processor could be filtering them out somewhere I did not find.
