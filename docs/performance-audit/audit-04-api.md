# Nornis API surface — performance & operating-cost audit

Scope: `src/Nornis.Api/` (Program.cs, Controllers, Middleware, Filters, Extensions,
Authentication, BackgroundServices, Contracts, appsettings), cross-checked against
`src/Nornis.Web` (ApiClient + Components) for what the client actually consumes, and
against `src/Nornis.Application` / `src/Nornis.Infrastructure` for what each endpoint
really costs.

Every finding below was confirmed by reading the code and tracing the request path.

**Summary: 5 High, 6 Medium, 5 Low.**

---

## Findings

### [SEVERITY: High] `/sources/activity` loads the entire world twice, every 15 seconds, per open tab — to return six integers

- **Where:** `src/Nornis.Api/Controllers/SourcesController.cs:111-145`
  (caller: `src/Nornis.Web/Components/Layout/NavMenu.razor:222-234`)
- **What:** The nav badge poller fires `GET /api/worlds/{id}/sources/activity` every 15s from
  `NavMenu`, which is on every page, for every connected circuit — unconditionally, with no
  guard on whether anything is actually in flight. It also re-fires on every `LocationChanged`.

  Traced, one call does all of this:
  1. `_sourceService.ListByWorldAsync` → `SourceRepository.ListByWorldAsync`
     (`src/Nornis.Infrastructure/Persistence/Repositories/SourceRepository.cs:34`) — `SELECT *`
     over every `Source` row in the world, **including `Body` and `DerivedText`**
     (`src/Nornis.Domain/Entities/Source.cs:21,47` — user session notes and machine-derived
     PDF/vision text, unbounded size), plus `.Include(s => s.Campaign)`.
  2. `reviewService.ListReviewQueueAsync` (`src/Nornis.Application/Services/ReviewService.cs:72`)
     — which calls **the same full source load a second time**.
  3. Up to 200 review proposals with their full `ProposedValueJson` (`ReviewService.cs:78`).
  4. `BuildProposalContextAsync` (`ReviewService.cs:100-130`): all review batches for the world,
     **all artifacts** for the world, and — when any `UpdateFact` / `UpdateRelationship` proposal
     is pending — **all facts** (`int.MaxValue`, `ReviewService.cs:120`) and all relationships.

  The controller then throws every byte of it away and returns
  `SourceActivityResponse(Ready, Queued, Processing, Failed, PendingProposals, PendingProposalsCapped)` —
  six integers.
- **Why it costs:** 4 requests/minute/tab, each ~6-10 SQL queries, two of which stream the
  world's entire note corpus out of Azure SQL. One GM with a browser open all day = ~5,700
  full-world scans. This is almost certainly the single largest DTU consumer and the largest
  driver of App Insights dependency-telemetry volume in the system. It also scales with world
  size, so the worst worlds (the ones with the most content) cost the most just to sit idle.
- **Fix:** Replace the endpoint body with aggregate queries — no entity materialization at all:
  ```csharp
  // ISourceRepository
  Task<IReadOnlyDictionary<SourceProcessingStatus,int>> CountByStatusAsync(
      Guid worldId, VisibilityFilter filter, CancellationToken ct);
  // IReviewProposalRepository
  Task<int> CountPendingAsync(Guid worldId, IReadOnlyList<Guid> allowedSourceIds, CancellationToken ct);
  ```
  backed by `GroupBy(...).Select(g => new { g.Key, N = g.Count() })` and `CountAsync(...)`.
  The visibility predicate that `CanSeeSource` applies in memory
  (`src/Nornis.Application/Services/SourceService.cs`) needs to move into the SQL `Where` — it is
  already expressible as a scope/owner predicate, exactly as `ArtifactFactRepository` does it
  (`ArtifactFactRepository.cs:~30`). Separately, add an `allowedSourceIds` projection query
  (`Select(s => s.Id)`) so `ListReviewQueueAsync` stops loading whole sources for an id list.
  Also gate the client poll: `NavMenu.PollActivityAsync` should back off (e.g. 60s) when the
  last response showed nothing in flight.
- **Effort:** medium
- **Risk:** The in-memory `CanSeeSource` / `GetAllowedSourceIds` predicates must be translated
  to SQL exactly, or badge counts leak private sources. Cover with tests per role
  (GM / Player-owner / Player-other / Observer) before and after.

---

### [SEVERITY: High] Every authenticated request pays 2-3 unconditional DB round-trips before the controller does any work

- **Where:** `src/Nornis.Api/Middleware/UserProvisioningMiddleware.cs:52`;
  `src/Nornis.Api/Filters/WorldMemberActionFilter.cs:35`;
  `src/Nornis.Application/Services/WorldService.cs:67,86` (and 18 more sites — see below)
- **What:** The pipeline in `Program.cs:314-317` runs `UseAuthentication` → `UseAuthorization` →
  `UserProvisioningMiddleware` for every request. The middleware does
  `userRepository.GetByAuth0SubjectIdAsync(sub)` — a SQL query — on **every** authenticated
  request, purely to turn the JWT `sub` into a `User` row that in practice only contributes
  `user.Id`. Then `WorldMemberActionFilter` (applied at controller level on essentially all
  world-scoped controllers) does a second query, `GetByWorldAndUserAsync(worldId, user.Id)`.
  Then several application services resolve the *same* membership a **third** time:
  `WorldService.GetByIdAsync:67`, `WorldService.UpdateAsync:86`, `WorldDeletionService:32`,
  `WorldExportService:47`, `CharacterService:38,179,232`, `WorldMemberService` (7 sites),
  `WorldInviteService:184` — all after the filter already put the member in
  `HttpContext.Items["WorldMember"]`.

  Confirmed non-issue for comparison: the JWT itself is validated locally. `Auth0Extensions.cs:27`
  sets `options.Authority`, so JwtBearer's `ConfigurationManager` fetches and caches OIDC
  discovery + JWKS with automatic refresh. There is no per-request network hop to Auth0.
- **Why it costs:** 2 queries of pure overhead on every API call, 3 on the world read/update
  paths. Combined with the polling in the previous finding and the chatty page loads below, the
  *majority* of SQL round-trips this system makes are auth plumbing, not data. Each also emits an
  App Insights dependency record.
- **Fix:** Two independent changes, both small:
  1. Cache the `sub → User` mapping. `builder.Services.AddMemoryCache()` and in the middleware
     `cache.GetOrCreateAsync($"user:{sub}", e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); ... })`.
     `User` is effectively immutable for request purposes (id + username + email) and is already
     read `AsNoTracking` (`UserRepository.cs:31-34`), so caching a detached copy is safe. Evict on
     the profile-update path only.
  2. Have the services that re-resolve membership accept the already-resolved
     `WorldMember`/`WorldRole` from the caller instead of re-querying. Most command records
     already carry `ActingUserRole` (e.g. `ArtifactListQuery`, `RenameArtifactCommand`) — apply
     that same pattern to `WorldService`, `WorldDeletionService`, `WorldExportService`,
     `CharacterService`.
- **Effort:** small (1) / medium (2)
- **Risk:** (1) A stale cache entry keeps a deleted user working for up to 10 minutes — acceptable,
  but pick the TTL deliberately. (2) Moving the authorization check from the service to the
  filter's result means the service no longer fails closed on its own; keep the role parameter
  mandatory (non-nullable) so a caller cannot forget it. Worth a `code-critic` pass — it touches
  authorization.

---

### [SEVERITY: High] `GET /canon` returns the world's entire canon; its only consumer renders six rows

- **Where:** `src/Nornis.Api/Controllers/CanonController.cs:29-63`;
  `src/Nornis.Application/Services/CanonService.cs:33-46`
- **What:** `GetCanonAsync` loads **all** artifacts for the world, then **all** facts for those
  artifacts with `maxPerArtifact: int.MaxValue` (`CanonService.cs:45`), then **all**
  relationships (`:46`), filters and sorts the union in memory, and returns the whole list with
  no limit and no paging.

  The only caller in the entire Web project is `Home.razor:397`, and what it does with the result is:
  ```csharp
  _canonFacts = canon.Where(c => c.Kind == "Fact").Take(3).ToList();
  _canonRelationships = canon.Where(c => c.Kind == "Relationship").Take(3).ToList();
  ```
  (`src/Nornis.Web/Components/Pages/Home.razor:414-416`). Six rows.
- **Why it costs:** The dashboard — the most-visited page — transfers and serializes the world's
  full knowledge graph on every load to show six list items. The fact query also builds a
  `WHERE ArtifactId IN (@p0…@pN)` with one parameter per artifact
  (`ArtifactFactRepository.ListByArtifactIdsAsync`), which degrades badly and hits SQL Server's
  2,100-parameter ceiling once a world exceeds ~2,000 artifacts.
- **Fix:** Add `[FromQuery] int? limit` and `[FromQuery] string? kind` to the controller, thread
  them into `CanonQuery`, and apply `OrderByDescending(UpdatedAt).Take(limit)` in SQL rather than
  after materialization. Default the limit (say 100) and cap it. Change `Home.razor` to request
  `?limit=3&kind=Fact` and `?limit=3&kind=Relationship`, or add a small combined
  `?factLimit=3&relationshipLimit=3`. Replace the `Contains(ids)` join with a real join on
  `Artifacts.WorldId == worldId` so no id list is materialized at all.
- **Effort:** small
- **Risk:** Low — one consumer, and the visibility filtering stays where it is. Confirm the
  truth-state filter still behaves when `limit` is applied before rather than after filtering
  (apply the visibility/truth-state predicates in SQL, then take).

---

### [SEVERITY: High] No HTTP response compression, on either end

- **Where:** `src/Nornis.Api/Program.cs` (no `AddResponseCompression` / `UseResponseCompression`
  anywhere in the repo — verified by repo-wide grep); `src/Nornis.Web/Program.cs:118-122`
  (`AddHttpClient<NornisApiClient>` uses the default primary handler, so
  `AutomaticDecompression` is `None` and no `Accept-Encoding` is sent)
- **What:** Kestrel does not compress by default and nothing enables it. Container Apps ingress
  does not compress backend-to-backend traffic either. Every JSON payload — the full artifact
  lists, full canon, 200-proposal review queues, source lists — crosses the wire uncompressed,
  both API→Web and Web→browser.
- **Why it costs:** These payloads are highly repetitive JSON (long property names, enum strings,
  GUIDs) and typically compress 80-90%. Two container apps talking over ingress means real egress
  bytes and real latency on every one of the polling requests above. This is a two-line change
  with a multiplier on every other finding in this report.
- **Fix:** In `src/Nornis.Api/Program.cs`:
  ```csharp
  builder.Services.AddResponseCompression(o => { o.EnableForHttps = true; o.Providers.Add<BrotliCompressionProvider>(); o.Providers.Add<GzipCompressionProvider>(); });
  ...
  app.UseResponseCompression();   // first middleware, before UseAuthentication
  ```
  and in `src/Nornis.Web/Program.cs`, make the typed client negotiate it:
  ```csharp
  }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
    .AddHttpMessageHandler<BearerTokenHandler>();
  ```
  Also enable compression in `Nornis.Web` itself for the Blazor Server payloads.
- **Effort:** trivial
- **Risk:** `EnableForHttps = true` is the BREACH-attack caveat; it is standard practice for a
  JSON API with no secrets reflected into responses, but note the decision. CPU cost is
  negligible relative to the bytes saved.

---

### [SEVERITY: High] 60 of 61 GET endpoints have no page limit, and list queries select whole entities including large text columns

- **Where:** `src/Nornis.Api/Controllers/*.cs` — 61 `[HttpGet]` actions, exactly one
  (`ArtifactsController.cs:115`, artifact search) accepts any `limit`. No endpoint accepts a
  page/offset/cursor. Representative offenders:
  `SourcesController.cs:72` (list sources), `ArtifactsController.cs:58` (list artifacts),
  `ArtifactsController.cs:260` (whole-world graph), `CanonController.cs:29`,
  `ReviewsController.cs:25` (capped at 200 in the service only),
  `UsersController.cs:22` (**every user in the system**, no filter, no limit — a global
  directory endpoint any authenticated user can call).
- **What:** Two compounding problems. (a) No caller can ask for less than everything.
  (b) The underlying repository queries materialize full entities rather than projecting: e.g.
  `SourceRepository.ListByWorldAsync:34` selects `Body` and `DerivedText` for the list view, but
  `SourcesController.ToSourceListItemResponse:621-635` never reads either. The wire response is
  slim; the DB→app leg is not.
- **Why it costs:** Cost grows linearly with world size with no ceiling — Azure SQL DTUs, app
  memory (a 5 MB `DerivedText` per source × N sources materialized per request), GC pressure, and
  serialization time. `GET /api/users` in particular becomes a full-table scan of the user table
  that gets slower for everyone as the product grows, and it is called from
  `WorldSettingsPanel.razor:438` to populate a picker.
- **Fix:** Two passes. First, add `.Select(...)` projections to the list-shaped repository methods
  so list endpoints never load `Body`/`DerivedText` — introduce a `SourceListItem` row type and
  return that from `ListByWorldAsync`. Second, add `?limit=`/`?before=` (keyset, ordered by
  `CreatedAt desc, Id desc`) to the list endpoints with unbounded growth: sources, artifacts,
  canon, review proposals, costs-by-*. For `GET /api/users`, require a `?q=` search term of ≥2
  characters and cap at 25 results.
- **Effort:** medium (large if done for all 60 at once — prioritize sources, artifacts, canon,
  users)
- **Risk:** Clients that today assume they receive everything (e.g. `Home.razor`'s in-memory
  `.Where(...).Take(...)`) will silently show partial data unless updated in the same change.
  Audit each consumer alongside its endpoint.

---

### [SEVERITY: Medium] Anonymous public GETs have no caching of any kind

- **Where:** `src/Nornis.Api/Controllers/PublicController.cs:59-273` — nine `[AllowAnonymous]`
  GET endpoints; `Program.cs` (no `AddOutputCache` / `AddResponseCaching`, no ETag or
  `Last-Modified` emitted anywhere in the repo — verified by grep)
- **What:** Every anonymous request re-resolves the slug (`ResolveAsync:282`,
  `_worldRepository.GetBySlugAsync` — a DB query per request, and the public pages call 2-3
  endpoints each) and then recomputes the full artifact list / graph / timeline / journey / canon
  from scratch. No `Cache-Control`, no ETag, no output cache.
- **Why it costs:** This is the surface most exposed to uncontrolled traffic — crawlers, social
  unfurlers, link previews, and anything a shared `/w/{slug}` link attracts. It is also the surface
  whose data changes least often (a published world changes when the GM accepts canon, i.e. rarely).
  The rate limiter (`Program.cs:264-272`, 120/min/IP) blunts abuse but does nothing about legitimate
  repeat traffic, and it runs *after* the DB work in a cache-less pipeline.
- **Fix:** `builder.Services.AddOutputCache(o => o.AddBasePolicy(b => b.Expire(TimeSpan.FromMinutes(5))));`
  and `app.UseOutputCache();` before `MapControllers()`, then `[OutputCache(Duration = 300, VaryByRouteValueNames = ["slug"], Tags = ["public"])]`
  on the read endpoints in `PublicController` (never on `Ask`). Evict with
  `IOutputCacheStore.EvictByTagAsync` when a world's canon changes, or just let the 5-minute TTL
  do it. Add `Cache-Control: public, max-age=60` so intermediaries help too.
- **Effort:** small
- **Risk:** A GM disabling public access, or flipping the demo kill switch
  (`DemoWorldOptions.PublicAccessEnabled`, `PublicController.cs:289`), stays visible for up to the
  TTL. Either tag-evict on those two writes or keep the TTL short (60s).

---

### [SEVERITY: Medium] Chatty screens: 4-6 endpoints to render one page, each carrying the full auth overhead

- **Where:**
  - `src/Nornis.Web/Components/Pages/Home.razor:394-399` — 5 parallel calls
    (storylines, artifacts, canon, review queue, ask suggestions), then a 6th
    (`EnsureContinuityLoadedAsync`, `:422`)
  - `src/Nornis.Web/Components/Pages/SourceDetail.razor:714-748` — up to 6 calls issued
    **sequentially**: campaigns → source → attachments → map → locations → replay → knowledge
  - `src/Nornis.Web/Components/Shared/CostsPanel.razor:213,237,258-260` — 4-5 calls
  - `src/Nornis.Web/Components/Shared/WorldSettingsPanel.razor:438,464-465,675,778` — 5 calls
- **What:** Each of these is a separate HTTP round trip that re-pays the user lookup + world
  member lookup from the auth finding above, plus TLS/ingress overhead. `SourceDetail` is the
  worst case: the calls are awaited one at a time, so the page's time-to-content is the *sum* of
  six round trips plus twelve auth queries, when the last four are all keyed on the same
  `(worldId, sourceId)`.
- **Why it costs:** ~12 redundant SQL queries and 5 extra round trips per dashboard load; the
  dashboard is the landing page. Latency is user-visible; the query volume is App Insights
  ingestion and DTU.
- **Fix:** Two composed read endpoints, both trivially derivable from what already exists:
  - `GET /api/worlds/{worldId}/dashboard` returning `{ storylines[10], recentArtifacts[4], canonFacts[3], canonRelationships[3], reviewQueue{count, top[5]}, suggestions, continuity }` — one filter pass, one response.
  - `GET /api/worlds/{worldId}/sources/{sourceId}/detail` returning source + attachments + map + locations + knowledge in one shot (the sub-fetches are already conditional on `source.Type`, so the server can make the same decision).

  As a cheaper interim step for `SourceDetail`, at minimum make the four post-source fetches
  concurrent with `Task.WhenAll` instead of sequential — that alone cuts the page's latency by
  roughly 3 round trips for zero API change.
- **Effort:** medium (composed endpoints) / trivial (the `Task.WhenAll` interim fix)
- **Risk:** Composed endpoints duplicate visibility logic if written by hand — build them by
  calling the same application services the individual endpoints call, so there is one
  authorization implementation.

---

### [SEVERITY: Medium] Tutorial checklist polling re-runs full-table detectors every 15 seconds

- **Where:** `src/Nornis.Application/Services/TutorialService.cs:87-122` and `:190-225`
  (`src/Nornis.Api/Controllers/TutorialController.cs:28-33`; caller
  `src/Nornis.Web/Components/Shared/TutorialChecklist.razor:137-146`)
- **What:** `GetChecklistAsync` loops over every not-yet-completed, non-client-reported step and
  calls `DetectAsync` for each. The detectors are:
  - `AddSessionSix` → `_sourceRepository.ListByWorldAsync` (all sources, with `Body`/`DerivedText`) then `.Any(s => s.CreatedAt > world.CreatedAt)` in memory (`:206-207`)
  - `WatchExtraction` → **the same full source load again** (`:212`)
  - `RevealSecret` → all review batches for the world, then `.Any(...)` in memory (`:222`)
  - `AskTheLoremaster` → all AI usage records for the world+user, then `.Any(r => r.Succeeded)` (`:196`)

  The checklist is polled every 15s while visible and not complete, i.e. for the whole duration
  of a new user's first session in a demo world — exactly when you most want the app to feel fast.
- **Why it costs:** Up to four full-table loads every 15 seconds during onboarding, two of them
  streaming note bodies, all to compute four booleans. Onboarding is also where demo worlds are
  created in bulk, so this scales with the thing you are trying to grow.
- **Fix:** Push each detector into SQL as an existence check:
  ```csharp
  Task<bool> AnyCreatedAfterAsync(Guid worldId, DateTimeOffset after, CancellationToken ct);
  Task<bool> AnyProcessedCreatedAfterAsync(Guid worldId, DateTimeOffset after, CancellationToken ct);
  Task<bool> AnyOfKindAsync(Guid worldId, string kind, CancellationToken ct);
  Task<bool> AnySucceededAsync(Guid worldId, Guid userId, AiOperationType type, CancellationToken ct);
  ```
  each a one-line `AnyAsync(predicate)`. `AnyDecidedByWorldAsync` (`:218`) already shows the
  correct shape — the other four just need to match it.
- **Effort:** small
- **Risk:** Low. Each detector's predicate is already explicit in the C#; the translation is
  mechanical. Cover with a test per step.

---

### [SEVERITY: Medium] A new Service Bus sender (and AMQP link) is created and torn down for every single message

- **Where:** `src/Nornis.Infrastructure/Messaging/ServiceBusExtractionQueueClient.cs:24`;
  `src/Nornis.Infrastructure/Messaging/ServiceBusLibraryIndexingQueueClient.cs:24`
- **What:** Both do `await using var sender = _serviceBusClient.CreateSender(QueueName);` per
  call. `ServiceBusSender` is designed to be created once and reused; disposing it closes the
  AMQP link. Both classes are registered as singletons (`Program.cs:227-228`) over a singleton
  `ServiceBusClient` (`:226`), so a cached sender is trivially safe here.
- **Why it costs:** Every enqueue pays an AMQP link-establishment round trip to Service Bus
  before the send. On the source-create path this is directly in the user's request latency, and
  on bulk import (`ImportSessionsController`) it is paid per note.
- **Fix:** Hoist the sender to a field created in the constructor (or a `Lazy<ServiceBusSender>`),
  drop the `await using`, and implement `IAsyncDisposable` on the client to dispose it at
  shutdown:
  ```csharp
  private readonly ServiceBusSender _sender;
  public ServiceBusExtractionQueueClient(ServiceBusClient c) => _sender = c.CreateSender(QueueName);
  public ValueTask DisposeAsync() => _sender.DisposeAsync();
  ```
- **Effort:** trivial
- **Risk:** None meaningful — `ServiceBusSender` is thread-safe and this is the documented usage.
  Make sure the DI registration stays `AddSingleton` so the container disposes it.

---

### [SEVERITY: Medium] The continuity-audit background service has no guard against concurrent replicas, and runs a tick immediately on every start

- **Where:** `src/Nornis.Api/BackgroundServices/ContinuityAuditBackgroundService.cs:33-62`
  (registered `Program.cs:176`)
- **What:** Three separate issues in one loop:
  1. `ExecuteAsync` calls `RunTickAsync` **before** the first `Task.Delay`, so a tick runs on
     every container start — including every deploy and every scale-out event.
  2. Eligibility (`:87-91`) is a plain read-then-act with no lock, no lease, and no row-level
     claim. Two API replicas ticking within the same window both see the same world as eligible
     and both call `auditService.RunAssessmentAsync` — which is a **paid Azure OpenAI call**
     (`nornis-ask`, $2.50/$15.00 per 1M tokens per `appsettings.json`). Container Apps scales the
     API on HTTP load; nothing in the repo pins it to one replica.
  3. `TimeSpan.FromHours(Math.Max(0.0, _options.TickIntervalHours))` (`:35`) floors at **zero**,
     not at a positive minimum. A misconfigured `ContinuityAudit:TickIntervalHours=0` turns this
     into a hot loop that re-scans every world in the database as fast as the DB will answer. The
     default is 1.0 (`ContinuityAuditOptions.cs:12`) so this is latent, not live — but it is a
     one-character config change away from being an incident.
- **Why it costs:** (2) is duplicate LLM spend with no ceiling other than the daily budget guard.
  (1) means a deploy day can trigger a burst of assessments. (3) is a config-triggered DTU fire.
- **Fix:** Move the work to the worker (which is presumably single-instance), or claim the work
  in the DB before running it — a conditional `UPDATE Worlds SET LastAuditClaimedAt = @now WHERE
  Id = @id AND (LastAuditClaimedAt IS NULL OR LastAuditClaimedAt < @cutoff)` and only proceed when
  one row was affected. Delay before the first tick rather than after. And floor the interval:
  `TimeSpan.FromHours(Math.Max(0.25, _options.TickIntervalHours))`.
- **Effort:** small (interval floor + delay-first) / medium (the claim)
- **Risk:** The claim column needs an additive migration. If the claim is written but the run
  crashes, the world is skipped until the cutoff passes — choose the cutoff to be shorter than
  `MinIntervalHours` so a lost run self-heals within a day.

---

### [SEVERITY: Medium] Per-request `Information` logs duplicate telemetry the OTel request pipeline already records

- **Where:** `src/Nornis.Api/Controllers/CostsController.cs:38,64,89,114,204`
- **What:** Five `_logger.LogInformation("Cost … requested. WorldId={WorldId}, UserId={UserId}, CorrelationId={CorrelationId}", …)` calls, one per cost endpoint. `appsettings.json` sets
  `"Default": "Information"`, so these ship. `Program.cs:34-40` wires `UseAzureMonitor()`, which
  already captures every request with its URL, route values, duration, status, and operation id —
  the correlation id in the message is the same `TraceIdentifier` the request telemetry carries.
  Opening the Costs panel fires 4-5 of these calls (`CostsPanel.razor:213,237,258-260`), so one
  panel open = 5 redundant trace records.
- **Why it costs:** App Insights bills on ingested GB (`appi-nornis`). These are pure duplicates —
  every field is already on the request record they hang off.
- **Fix:** Delete them, or drop them to `LogDebug` (which the `Information` default filters out in
  production). If per-world cost attribution in logs is genuinely wanted, add `WorldId` as a
  telemetry-initializer property on the request record instead of emitting a second record.
- **Effort:** trivial
- **Risk:** None. Nothing queries these; the same data is queryable from `requests`.

---

### [SEVERITY: Low] `/health` does a database round-trip on every liveness ping, with caching explicitly disabled

- **Where:** `src/Nornis.Api/Program.cs:74-75,323-327`;
  `src/Nornis.Infrastructure/Persistence/PendingMigrationsHealthCheck.cs:28-33`
- **What:** The single registered health check calls `Database.GetPendingMigrationsAsync()`, which
  queries `__EFMigrationsHistory` and compares it to the assembly's migration list. `/health` is
  mapped with `AllowCachingResponses = false`, and it is what the App Insights availability test
  pings. It is also what `NornisApiClient.GetHealthAsync` calls.
- **Why it costs:** A standard availability test (5 locations × 5 min) is ~1,440 probes/day, each
  opening a connection and running a query, plus a DbContext scope. Small in absolute terms, but
  it is unconditional, permanent, and grows with every probe source added.
- **Fix:** The migration check answers a question that can only change at process start — cache the
  result. Compute it once in a hosted service at startup into a singleton flag and have the health
  check read the flag, or memoize with a long TTL:
  ```csharp
  // singleton
  private volatile bool? _upToDate;
  ```
  Alternatively split into `/health/live` (no dependencies, for the probe) and `/health/ready`
  (the migration check, for the deploy gate) and point the availability test at `/health/live`.
- **Effort:** trivial
- **Risk:** Caching at startup is *more* correct for this check's stated purpose (catching a
  missed pre-deploy migration step), since the answer cannot change while the process runs.

---

### [SEVERITY: Low] `WorldMemberFilter` is dead code

- **Where:** `src/Nornis.Api/Filters/WorldMemberFilter.cs` (whole file)
- **What:** An `IEndpointFilter` implementation for minimal APIs. The project uses MVC controllers
  exclusively and applies `WorldMemberActionFilter` (the `IAsyncActionFilter` twin) instead. Grep
  across `src/` and `tests/` finds zero references except a doc comment in
  `HttpContextExtensions.cs:24` that names the wrong class. It also duplicates the membership
  logic, so any authorization fix has to be made twice or will silently diverge.
- **Why it costs:** No runtime cost — maintenance and correctness cost. Two copies of an
  authorization check is a latent security bug.
- **Fix:** Delete the file; update the exception message in `HttpContextExtensions.cs:24` to name
  `WorldMemberActionFilter`.
- **Effort:** trivial
- **Risk:** None (verified zero references).

---

### [SEVERITY: Low] `AzureBlobStorageService` performs a synchronous network call in its constructor

- **Where:** `src/Nornis.Infrastructure/Storage/AzureBlobStorageService.cs:32-33` (registered
  singleton at `src/Nornis.Api/Program.cs:244-248`)
- **What:** The constructor calls `containerClient.CreateIfNotExists(PublicAccessType.None)` —
  a blocking HTTP round trip to Azure Storage. Because the singleton is resolved lazily, this runs
  on whichever request thread first touches blob storage (a library upload or a world export).
- **Why it costs:** Blocks a thread-pool thread on network I/O during a live request, and adds
  a one-off latency spike to an unlucky user's first upload. Under load this is exactly the shape
  that causes thread-pool starvation.
- **Fix:** Drop the check entirely (the container is created once by infrastructure/deploy, not by
  the app), or move it into an `IHostedService.StartAsync` that awaits
  `CreateIfNotExistsAsync`, so the cost is paid at startup off the request path.
- **Effort:** trivial
- **Risk:** If the container genuinely does not exist in some environment, removing the check
  turns a startup surprise into a runtime 500. Prefer the hosted-service option.

---

### [SEVERITY: Low] `GET /tutorial/session-six` re-opens and decompresses the demo template zip on every call

- **Where:** `src/Nornis.Application/Services/TutorialService.cs:169-186`
  (`src/Nornis.Api/Controllers/TutorialController.cs:44-51`)
- **What:** Every request calls `_templateProvider.OpenRead()` (a `File.OpenRead`,
  `FileDemoWorldTemplateProvider.cs:33`), constructs a `ZipArchive`, finds the entry, and reads it
  to a string. `IDemoWorldTemplateProvider` is already a singleton (`Program.cs:124`) and the file
  is immutable — it ships in the image. `IsAvailable` also does a synchronous `File.Exists` per call.
- **Why it costs:** Disk I/O + zip inflate + a full string allocation per request, for content
  that cannot change for the lifetime of the container.
- **Fix:** Memoize in the singleton provider — read the entry once into a cached `string` behind a
  `Lazy<Task<string>>`, or add `Task<string?> GetSessionSixTextAsync()` to
  `IDemoWorldTemplateProvider` and cache there. Also cache the `File.Exists` result.
- **Effort:** trivial
- **Risk:** None — the file is part of the immutable image.

---

### [SEVERITY: Low] `AddDbContext` is not pooled, and `Home`'s parallel loads carry no `CancellationToken`

- **Where:** `src/Nornis.Api/Program.cs:78-79`; `src/Nornis.Web/Components/Pages/Home.razor:394-399`
- **What:** (a) `AddDbContext<NornisDbContext>` allocates and configures a fresh context per scope;
  `AddDbContextPool` reuses instances and skips the model/service-provider resolution per request.
  With 2-3 contexts' worth of work on every request (see the auth finding), the per-request setup
  cost is paid constantly. (b) The five dashboard tasks are started with no `ct`, so a user who
  navigates away mid-load leaves five in-flight API calls — each of which is one of the expensive
  unbounded queries above — running to completion server-side. Controllers thread
  `CancellationToken` correctly throughout (verified across all 26 controllers); the gap is on the
  client side.
- **Why it costs:** (a) is a small constant-factor win across the highest-frequency path in the
  system. (b) means abandoned dashboard loads keep burning DTUs and Web thread time.
- **Fix:** (a) `builder.Services.AddDbContextPool<NornisDbContext>(...)` — measure first; pooling
  requires the context to have no per-instance mutable state beyond what's reset (verify
  `NornisDbContext` has no injected scoped state). (b) Give `LoadDashboardAsync` a
  `CancellationTokenSource` tied to the component lifetime and pass its token to all five calls,
  matching what `NavMenu` and `SourceDetail` already do.
- **Effort:** trivial
- **Risk:** (a) `AddDbContextPool` misbehaves if the DbContext captures scoped services in its
  constructor — check before switching. (b) None.

---

## Verified as *not* a problem

Recording these so they don't get re-audited:

- **JWT validation is local and cached.** `Auth0Extensions.cs:27` sets `options.Authority`, so
  JwtBearer's `ConfigurationManager` handles OIDC discovery + JWKS retrieval with its own cache
  and refresh interval. No per-request network hop to Auth0.
- **`JsonSerializerOptions` are never allocated per call.** Every instance in the codebase is
  `private static readonly` (`ProposalApplicator.cs:22`, `ReviewService.cs:231`,
  `DemoWorldService.cs:28`, `WorldExportService.cs:18`, `ProposalValidator.cs:19`,
  `ContinuityFixService.cs:74`, `RelationshipBackfillService.cs:29`,
  `AzureOpenAiExtractionClient.cs:20`). No `ReferenceHandler.Preserve` anywhere.
- **Expensive clients are singletons.** `ServiceBusClient` (`Program.cs:226`),
  `AzureOpenAIClient`'s chat and embedding clients (`:186,195`), `IBlobStorageService` (`:244`),
  `IProposalValidator` (`:160`), `IInviteCodeGenerator` (`:132`),
  `IDemoWorldTemplateProvider` (`:124`). No scoped dependency is captured by a singleton — the
  background service correctly opens its own scope per tick
  (`ContinuityAuditBackgroundService.cs:66`).
- **No sync-over-async on request threads.** Repo-wide grep for `.Result` / `.Wait()` /
  `GetAwaiter().GetResult()` across Api, Application, and Infrastructure returns only
  `ActionExecutingContext.Result` assignments.
- **No string interpolation in log calls.** All logging uses structured templates.
- **The dev-auth bypass cannot run in production.** Gated on
  `IsDevelopment() && Auth0:Domain == "your-tenant.auth0.com"` (`Program.cs:43,306`).

---

## Unverified / worth a look

- **API replica count.** Finding #10's duplicate-LLM-spend risk depends on `ca-nornis-api`
  running more than one replica. `.github/workflows/deploy.yml` only issues
  `az containerapp update --image`; the scale configuration lives outside the repo. Confirm with
  `az containerapp show -g rg-nornis -n ca-nornis-api --query properties.template.scale`. Note
  that even at `minReplicas: 1`, a rolling deploy briefly runs two revisions.
- **Actual payload sizes.** I confirmed *which* columns and rows each endpoint loads, but not the
  real byte counts in the production database. `SELECT AVG(DATALENGTH(Body) + DATALENGTH(DerivedText)), COUNT(*) FROM Sources GROUP BY WorldId`
  would size finding #1 precisely and tell you whether it is a $10/month problem or a $200/month one.
- **Container Apps ingress compression.** I confirmed the app does not compress. Whether the
  Container Apps ingress (Envoy) applies any compression to backend responses is not determinable
  from the repo; the client-side `AutomaticDecompression = None` means it would not be requested
  anyway, so finding #4 stands regardless.
- **Whether an availability test actually pings `/health`.** Inferred from the health endpoint's
  design comments and prior project context, not from anything in the repo. Confirm before sizing
  finding #12.
- **`ArtifactService.GetGraphAsync` cost.** `ArtifactsController.cs:260` and
  `PublicController.cs:123` expose a whole-world graph, and `ArtifactDetail.razor:896` fetches it
  to render a one-hop neighborhood (the BFS trim happens in JS, per the comment at `:880`). I did
  not read the service implementation in full, but the shape — full graph over the wire to render
  one node's neighbors — matches the over-fetch pattern in findings #3 and #5 and is worth its own
  look.
