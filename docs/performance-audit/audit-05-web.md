# Nornis Web Client — Performance & Cost Audit

**Scope:** `src/Nornis.Web/` — read-only static analysis. No dev servers were started.

## Hosting model

**Blazor Server (interactive server rendering), globally applied, with prerendering ON.**

- `src/Nornis.Web/Nornis.Web.csproj` — `Microsoft.NET.Sdk.Web`, net10.0 (via `Directory.Build.props`). No `Microsoft.AspNetCore.Components.WebAssembly*` package, no `.Client` project. So **no WASM findings apply** — there is no trimming/AOT/publish payload question, and no `blazor.boot.json` to shrink.
- `Program.cs:22-23` — `AddRazorComponents().AddInteractiveServerComponents()`; `Program.cs:171-172` — `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
- `Components/App.razor:16,20` — `<HeadOutlet @rendermode="InteractiveServer" />` and `<Routes @rendermode="InteractiveServer" />`. This is the static `RenderMode.InteractiveServer` field, whose `prerender` defaults to `true`. **Every route in the app, public and authenticated, is prerendered and then re-rendered interactively.** There is no `PersistentComponentState` anywhere in the project (verified by grep).
- Consequences that shape the rest of this report: UI diffs travel over SignalR (so re-render storms cost server CPU + websocket bytes, not client CPU); component state lives per-circuit; and every full page load executes each component's `OnInitializedAsync`/`OnParametersSetAsync` **twice**.

Static asset totals (measured):

| File | Raw | gzip -9 |
|---|---|---|
| `_content/MudBlazor/MudBlazor.min.css` | 580,975 B (567 KB) | 62,439 B |
| `wwwroot/lib/tiptap.bundle.min.js` | 470,202 B (459 KB) | 149,887 B |
| `wwwroot/lib/force-graph.min.js` | 177,267 B (173 KB) | 57,255 B |
| `wwwroot/app.css` | 90,956 B (89 KB) | 16,729 B |
| `_content/MudBlazor/MudBlazor.min.js` | 49,823 B | — |
| `wwwroot/js/*.js` (8 files) | ~62 KB | — |
| **wwwroot total** | **857 KB** | |

Largest wwwroot images: `images/logo.png` 18.7 KB, `images/transparent-logo.png` 9.3 KB, `favicon.png` 7.8 KB — these are fine, not a finding.

---

## Findings

### [SEVERITY: High] Prerendering makes every page load its data twice
- **Where:** `src/Nornis.Web/Components/App.razor:16` and `:20`; enabled by `src/Nornis.Web/Program.cs:171-172`
- **What:** `@rendermode="InteractiveServer"` uses the default `prerender: true`. On a full page load (first visit, F5, deep link, OIDC redirect back from Auth0) ASP.NET renders the whole component tree statically inside the HTTP request — running every `OnInitializedAsync` / `OnParametersSetAsync` and every API call in them — then throws that tree away, starts a circuit with a **fresh DI scope**, and constructs every component again from scratch, running all of it a second time. `WorldState`, `ActivitySignal`, `AskState` and `ViewAsState` are all `AddScoped` (`Program.cs:124-127`), so their per-circuit de-duplication caches do not survive the handoff — the prerender scope's `WorldState._loaded` is discarded.
  The codebase already knows this happens: `Components/Shared/SessionWrapUpCard.razor:256-262` says *"the interactive render re-runs LoadAsync"* out loud, and works around the interop consequence without addressing the double fetch.
- **Why it costs:** Measured against a full load of `/` for a GM: `GET /api/worlds` fires **twice**, `GET /api/onboarding` fires **four times** (2 components × 2 passes — see next finding). On the public `/w/{slug}/artifacts` page — anonymous, uncached, the traffic most likely to be crawled or spiked — `GET /api/public/worlds/{slug}` and `GET /api/public/worlds/{slug}/artifacts` each fire twice, so 4 API requests and 4 SQL round trips to render one anonymous page view. Every world-scoped page (`/sources`, `/artifacts`, `/timeline`, `/admin`, `/library`, `/review`) doubles its load set the same way on any hard navigation. Additionally, fire-and-forget poll loops are started in the prerender pass too (`NavMenu.razor:210`, `Sources.razor:146`, `TutorialChecklist.razor:128`, `Import.razor:612`), so each full load briefly spins up two copies of every polling timer.
- **Fix:** Two options, in order of preference.
  1. Keep prerendering (good for SEO on the public `/w/{slug}` pages and for first paint) and persist the prerendered data across the handoff:
     ```csharp
     // Program.cs — nothing to add; PersistentComponentState is already registered.
     // In each page/state holder:
     @inject PersistentComponentState AppState
     private PersistingComponentStateSubscription _sub;
     protected override async Task OnInitializedAsync()
     {
         _sub = AppState.RegisterOnPersisting(() =>
         {
             AppState.PersistAsJson("worlds", Worlds.Worlds);
             return Task.CompletedTask;
         });
         if (AppState.TryTakeFromJson<IReadOnlyList<WorldSummary>>("worlds", out var restored)) { /* seed WorldState */ }
         else { await Worlds.EnsureLoadedAsync(); }
     }
     ```
     Best applied once inside `WorldState` (covering `/api/worlds` and the continuity assessment) and once for `/api/onboarding`, which together are the bulk of the duplication.
  2. If the prerendered HTML has no real value here — `MainLayout.razor:38-47` already holds the entire page body behind a `Worlds.Ready` spinner during prerender, so the prerendered output for authenticated pages is *literally a loading spinner* — just turn it off for the authenticated shell:
     ```razor
     <Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />
     ```
     That halves the API load on every authenticated full page load for zero visual change. Consider keeping prerender for the public `/w/{slug}` routes only (they render real content and want to be indexable) by applying the render mode per-component rather than at `Routes`.
- **Effort:** small (option 2) / medium (option 1)
- **Risk:** Option 2 removes server-rendered HTML for authenticated pages — harmless here because the boot gate already suppresses it, but it does mean a blank frame until the circuit connects. Option 1 risks serving stale persisted state if the persist/restore keys drift; scope keys per world id.

---

### [SEVERITY: High] `GET /api/onboarding` is fetched by two different components on the same page
- **Where:** `src/Nornis.Web/Components/Shared/TutorialChecklist.razor:124` and `src/Nornis.Web/Components/Shared/OnboardingPrompt.razor:67`
- **What:** `TutorialChecklist` is mounted unconditionally in `MainLayout.razor:50`, so it is present on every authenticated page and calls `Api.GetOnboardingAsync()` in `OnInitializedAsync`. `OnboardingPrompt` is mounted at `Home.razor:11` and calls the identical `Api.GetOnboardingAsync()` in its own `OnInitializedAsync`. Neither caches the result anywhere shared. They read different fields off the same DTO (`TutorialDismissed` vs `PromptSeen`).
- **Why it costs:** 2 identical requests per interactive render of `/`, and 4 per full page load once the prerender pass is counted. `/` is the app's landing route, so this is the single most-hit duplicated endpoint. The layout copy also re-runs whenever the circuit restarts.
- **Fix:** Cache the onboarding DTO in a scoped state holder next to `WorldState`, with the same shared-task idempotency pattern already used for `EnsureLoadedAsync` / `EnsureContinuityLoadedAsync`:
  ```csharp
  public sealed class OnboardingState(NornisApiClient api)
  {
      private Task<ApiResult<OnboardingStateDto>>? _task;
      public Task<ApiResult<OnboardingStateDto>> GetAsync() => _task ??= api.GetOnboardingAsync();
      public void Invalidate() => _task = null;   // after DismissTutorial / MarkPromptSeen
  }
  ```
  Register it `AddScoped` in `Program.cs` beside the other four state services and have both components call it.
- **Effort:** trivial
- **Risk:** Dismissal must invalidate the cache or the checklist lingers for the rest of the circuit; `OnboardingPrompt.Close()` and `TutorialChecklist.DismissAsync()` already set local flags, so the cache staleness is cosmetic at worst.

---

### [SEVERITY: High] Static assets are served uncompressed and without fingerprinted caching
- **Where:** `src/Nornis.Web/Program.cs:151` (`app.UseStaticFiles();`)
- **What:** The app uses the legacy `UseStaticFiles()` rather than .NET 9/10's `MapStaticAssets()`, and registers neither `AddResponseCompression()` nor `UseResponseCompression()` (verified by grep across the whole project — zero hits). `wwwroot/service-worker.js` explicitly documents that it caches nothing ("This deliberately does NOT cache anything"), so there is no client-side fallback. Cache-busting is done by hand with query strings (`App.razor:13,23-32` — `?v=4`, `?v=2`…), which browsers honour but which give no immutable-caching benefit.
- **Why it costs:** A cold visit to any Nornis page transfers roughly **1.45 MB of raw CSS+JS** where ~370 KB would do. Measured deltas: MudBlazor.min.css 581 KB → 62 KB gzip (−519 KB), tiptap.bundle 470 KB → 150 KB (−320 KB), force-graph 177 KB → 57 KB (−120 KB), app.css 91 KB → 17 KB (−74 KB). That is **~1.03 MB of egress saved per cold page view**, which is both a first-paint latency win and a direct Azure Container Apps egress cost line. It also affects the anonymous public `/w/{slug}` pages, which are the ones most likely to be linked around.
- **Fix:** Two lines. Replace `app.UseStaticFiles()` with `app.MapStaticAssets()` and switch the manual cache-busters to fingerprinted asset URLs:
  ```csharp
  app.MapStaticAssets();     // Program.cs:151 — precompresses to .gz/.br at build, emits
                             // immutable Cache-Control + ETag, and fingerprints filenames.
  ```
  ```razor
  @* App.razor *@
  <link rel="stylesheet" href="@Assets["app.css"]" />
  <script src="@Assets["lib/tiptap.bundle.min.js"]"></script>
  ```
  `MapStaticAssets` covers `_content/MudBlazor/*` too, since those arrive through the static web assets manifest. If ingress-level compression is later confirmed to exist, the fingerprinted immutable caching is still worth having on its own.
- **Effort:** small
- **Risk:** `Assets[...]` throws at render time for a path not in the manifest, so every `<link>`/`<script>` in `App.razor` must be converted together and the app smoke-tested once. The hand-rolled `?v=` busters must be removed at the same time or you get double-versioned URLs.

---

### [SEVERITY: High] 632 KB of editor and graph JavaScript is loaded on every page, including the public marketing pages
- **Where:** `src/Nornis.Web/Components/App.razor:21-32`
- **What:** `App.razor` is the single host document for every route in the app — authenticated, public marketing (`/welcome`, `/about`, `/features`, `/privacy`, `/terms`, `/licenses`, `/changelog`) and anonymous world pages (`/w/{slug}`). It unconditionally emits eleven classic (non-`defer`, non-`async`, non-`module`) `<script>` tags, including `lib/force-graph.min.js` (177 KB) and `lib/tiptap.bundle.min.js` (470 KB).
  Actual usage is narrow: force-graph is only touched by `nornisGraph.render`, called from `Components/Shared/ArtifactGraph.razor:135`, which is mounted on exactly three pages (`Artifacts.razor:100`, `ArtifactDetail.razor:276`, `PublicWorldArtifacts.razor:56`) and only when the user picks the graph view. TipTap is only touched by `nornisEditor.init` from `Components/Shared/NotesEditor.razor:112`, mounted on `Capture`, `SourceDetail`, `InkCapture` and `PublicWorldSourceDetail`.
- **Why it costs:** 647 KB raw (207 KB even after the compression fix above) downloaded, parsed and executed by every visitor to the landing page — people who have not signed in and may never see a graph or an editor. Both scripts run to completion before `_framework/blazor.web.js` at line 34 is even fetched, delaying circuit start on every load.
- **Fix:** Load them on demand from the components that need them. Both JS files already use the module-object pattern (`window.nornisGraph`, `window.nornisEditor`), so the smallest change is a lazy loader invoked from `OnAfterRenderAsync`:
  ```javascript
  // wwwroot/js/nornis-lazy.js — kept in App.razor (a few hundred bytes)
  window.nornisLazy = { loaded: {}, load(src) {
      return this.loaded[src] ??= new Promise((ok, err) => {
          const s = document.createElement('script');
          s.src = src; s.onload = ok; s.onerror = err;
          document.head.appendChild(s);
      });
  }};
  ```
  ```csharp
  // ArtifactGraph.razor, before the first nornisGraph.render call
  await JS.InvokeVoidAsync("nornisLazy.load", "lib/force-graph.min.js");
  await JS.InvokeVoidAsync("nornisLazy.load", "js/nornis-graph.js");
  ```
  The cheaper half-measure, if lazy loading feels risky: add `defer` to all eleven tags so they stop blocking `blazor.web.js`. That is a one-word change per line and recovers most of the startup-latency cost without touching component code.
- **Effort:** small (defer) / medium (lazy load)
- **Risk:** Lazy loading introduces an await before the first render of a graph or editor — if a component calls `nornisGraph.render` without awaiting the loader first it will fail with "nornisGraph is not defined". Each of the four call sites must be converted. `defer` alone is near-zero risk since no inline script depends on these globals at parse time.

---

### [SEVERITY: High] `StorylineTimelineChart.Rows` is a heavy property getter re-evaluated hundreds of times per render
- **Where:** `src/Nornis.Web/Components/Shared/StorylineTimelineChart.razor:300-383` (the getter), consumed at lines `15`, `53`, `54`, `60`, `61`, `69`, `74`, `78`, `98`, `147`, `155`, `156`, `239`, `404`, `490`, `533`
- **What:** `Rows` is a computed property, not a cached field. Each evaluation runs: two `Where` passes plus `ToList` over all lanes, a `ToHashSet`, a `GroupBy` + `ToDictionary` with a nested `OrderBy().ThenBy()` per group, a second filtered `ToList` for roots, a `GroupBy` over roots with a `SelectMany(Family).Min()` recursive tree walk per band, an `OrderBy().ThenBy().ToList()`, and finally a recursive `AddFamily` walk building a fresh `List<Row>`.
  Three derived properties funnel back into it: `LaneBottom` (line 404) touches `Rows` **three times** per evaluation (`Rows.Count`, `Rows[^1]` twice), and `TotalHeight` (line 406) is `LaneBottom + AxisHeight`. Those derived properties are then used **inside render loops**: `LaneBottom` appears twice inside `@foreach (var month in MonthTicks)` (lines 60-61), once inside `@foreach (var session in Data.Sessions.Where(...))` (line 69), and twice inside `@foreach (var session in Data.Sessions)` (line 78). `Connectors` (line 490) and `DrawableLinks` (line 533) each capture `var rows = Rows;` again.
- **Why it costs:** For a two-year campaign — 24 month ticks, 40 sessions — `LaneBottom` is evaluated roughly `(24 × 2) + 1 + 40 + 2 + (40 × 2) ≈ 171` times, and each of those triggers 3 full `Rows` computations. Add the direct uses and that is **on the order of 500 complete rebuilds of the lane tree for a single render of the chart**, all allocating. On Blazor Server this is server CPU and GC pressure on the shared app instance, and it re-runs on every status-filter chip click on `/timeline` (`Timeline.razor:110` passes `StatusFilter="_selectedStatuses"`, and `Rows` filters on it) and on every `StateHasChanged` from any sibling.
- **Fix:** Memoize on the inputs. The component already knows what invalidates the layout (`Data` and `StatusFilter`), so compute once per parameter set:
  ```csharp
  private IReadOnlyList<Row>? _rowsCache;
  private (object? Data, int FilterHash) _rowsKey;

  private IReadOnlyList<Row> Rows
  {
      get
      {
          var key = ((object?)Data, StatusFilter is null ? 0 : string.Join('|', StatusFilter.Order()).GetHashCode());
          if (_rowsCache is null || !key.Equals(_rowsKey)) { _rowsCache = BuildRows(); _rowsKey = key; }
          return _rowsCache;
      }
  }
  private IReadOnlyList<Row> BuildRows() { /* the existing body, verbatim */ }
  ```
  Also hoist `LaneBottom`/`TotalWidth`/`TotalHeight` into locals before the render loops rather than re-reading the property inside each iteration. The same memoization pattern is already used correctly elsewhere in this codebase (`Artifacts.razor` / `PublicWorldArtifacts.razor:186-203` `FilteredGraph`), so it matches house style.
- **Effort:** small
- **Risk:** A stale cache if a mutation reaches `Data.Lanes` in place rather than replacing the `Data` reference. `Timeline.razor` reloads the whole DTO after a reparent (`LoadTimelineAsync`), so reference identity is a sound key — but confirm no in-place edits before shipping.

---

### [SEVERITY: Medium] The nav activity badge refetches on every navigation, every write, and every 15 seconds
- **Where:** `src/Nornis.Web/Components/Layout/NavMenu.razor:205-213` (three subscriptions), `:226` (15 s `PeriodicTimer`), `:257` (`GetSourceActivityAsync`), and `src/Nornis.Web/ApiClient/NornisApiClient.cs:716-719` (`_activity.Notify()` on every successful non-GET)
- **What:** `RefreshActivityAsync` is wired to four independent triggers: `Worlds.Changed`, `ActivitySignal.Changed`, `NavigationManager.LocationChanged`, and a 15-second `PeriodicTimer`. `NornisApiClient.SendCoreAsync` calls `_activity.Notify()` after *every* successful write of any kind, deliberately ("the cost of being wrong is one cheap GET that the nav de-duplicates"). `ReviewQueuePanel.razor:235` calls `Activity.Notify()` again on every load *and* every decision, on top of the write's own notification.
  The `_activityRefreshing` guard (`:249-254`) only drops *concurrent* duplicates; sequential ones all go through. During boot, `Worlds.Changed` fires three times (`WorldState.cs:157/168` from `ReloadAsync`, `:142` from `RestoreSelectionCoreAsync`, `:239` from `LoadContinuityCoreAsync`) and each one calls `RefreshActivityAsync` unguarded by world id — the third fetches numbers identical to the second.
- **Why it costs:** A baseline of **4 requests/minute per open tab**, forever, for a completely idle user — plus one per page navigation, plus one or two per write. A GM with the app open on a laptop and a tablet during a session generates ~480 `sources/activity` requests per hour doing nothing. Each is an authenticated API call with a SQL aggregate behind it. This is a steady-state operating cost that scales with concurrent tabs, not with usage.
- **Fix:** Three cheap changes, any of which helps:
  1. Suppress redundant fetches with a short freshness window instead of only a concurrency guard:
     ```csharp
     private DateTime _activityFetchedAt;
     if (_activityRefreshing || DateTime.UtcNow - _activityFetchedAt < TimeSpan.FromSeconds(3)) return;
     ```
     This collapses the boot triple-fire and the write-then-navigate double-fire into one request without changing perceived freshness.
  2. Back off the poll when nothing is in flight: 15 s while `_activity is { InFlight: > 0 } or { PendingProposals: > 0 }`, 60 s otherwise. Extraction is the only thing that changes these numbers without the user acting, and only when something is queued.
  3. Stop polling when the tab is hidden (`document.visibilityState`) — a one-call interop plus a `visibilitychange` listener would remove the cost of every backgrounded tab.
- **Effort:** small
- **Risk:** Longer intervals mean a worker-side extraction finishing takes longer to show up in the sidebar count. The `ActivitySignal` path already covers everything this browser did, so only other-browser/worker changes are affected.

---

### [SEVERITY: Medium] Independent requests are issued in sequence instead of in parallel
- **Where:** several; the worst is `src/Nornis.Web/Components/Pages/SourceDetail.razor:714-747`
- **What:** `SourceDetail.LoadAsync` is a five-stage serial waterfall: `GetCampaignsAsync` (714) → `GetSourceAsync` (717) → `GetSourceAttachmentsAsync` (730) → `GetSourceMapAsync` (735) → `GetSourceLocationsAsync` (742) → `RefreshReplayAsync` (746) → `LoadKnowledgeAsync` (774). Only stages 3+ genuinely depend on stage 2 (they branch on `_source.Type`); campaigns is fully independent of the source, and attachments/map/locations/replay/knowledge are independent of each other.
  Others confirmed:
  - `Components/Shared/WorldSettingsPanel.razor:428-440` — `LoadMembersAsync` → `LoadCampaignsAndCharactersAsync` → `LoadInvitesAsync` → `GetUsersAsync`: four serial stages, five requests, all independent. (The inner campaigns+characters pair at `:464-467` is correctly parallelized — the pattern is already known here.)
  - `Components/Shared/CostsPanel.razor:213-240` — `GetCostSummaryAsync` → `GetCostsByWorldAsync` → three parallel breakdowns: three serial stages where two would do.
  - `Components/Pages/Sources.razor:144-145` — `LoadCampaignsAsync` then `LoadSourcesAsync`, independent.
  - `Components/Pages/Capture.razor:192-196`, `Components/Pages/Timeline.razor:366-367` — same shape, single extra call each.
- **Why it costs:** Each stage is a full HTTP round trip from the Web container to the API container, so a Source detail page pays 4–5 sequential RTTs before it paints. At an intra-region 15–30 ms RTT that is 60–150 ms of pure serialization on the most-visited detail page; on a cold API container it is much worse. It also holds the Blazor circuit's render loop, so the page stays on its skeleton the whole time.
- **Fix:** `Task.WhenAll` at each independent stage. `Home.razor:395-400` and `CostsPanel.razor:258-261` already do exactly this and comment on why it is safe ("Separate HTTP requests, each with its own server-side DbContext scope"), so this is applying an existing house pattern:
  ```csharp
  // SourceDetail.LoadAsync
  var campaignsTask = Api.GetCampaignsAsync(worldId);
  var sourceTask    = Api.GetSourceAsync(worldId, SourceId);
  await Task.WhenAll(campaignsTask, sourceTask);
  // …then, once _source.Type is known, one more WhenAll over attachments/map/locations
  // and a final WhenAll over replay + knowledge.
  ```
- **Effort:** small per site, medium across all of them
- **Risk:** Low — these are separate HTTP calls with independent server-side scopes. The one thing to preserve is the `_source.Type` branch: attachments/map/locations must still be gated on the loaded type, so it stays a two-phase parallel load rather than one flat `WhenAll`.

---

### [SEVERITY: Medium] Public `/w/{slug}` pages refetch the world card on every tab change, serialized behind the page's own fetch
- **Where:** `src/Nornis.Web/Components/Pages/Public/World/PublicWorldFrame.razor:53-65`, consumed by `PublicWorld.razor:8`, `PublicWorldArtifacts.razor:7`, `PublicWorldTimeline.razor`, `PublicWorldLocations.razor`, `PublicWorldSources.razor`
- **What:** `PublicWorldFrame` fetches `GetPublicWorldAsync(Slug)` in its own `OnParametersSetAsync`, guarded only by `Slug == _loadedSlug` on its **own instance**. Each of the five public section pages is a separate routable component that instantiates its own `PublicWorldFrame`, so navigating Overview → Codex → Timeline destroys and recreates the frame each time and refetches the same immutable world card. There is no shared cache — the authenticated side has `WorldState` for exactly this job; the public side has nothing.
  Worse, the two fetches are serialized: Blazor awaits the page's `OnParametersSetAsync` (e.g. `PublicWorldArtifacts.razor:245-256`, `GetPublicArtifactsAsync`) *before* rendering its children, so `PublicWorldFrame` does not even start `GetPublicWorldAsync` until the artifacts list has come back.
- **Why it costs:** 2 sequential API round trips per public page view rather than 1 parallel pair — doubled again to 4 by the prerender finding above. A visitor browsing four tabs of a public world costs 8 world-card fetches for data that never changes during the visit. This is anonymous traffic with no per-user rate limiting to fall back on.
- **Fix:** Add a scoped `PublicWorldState` mirroring `WorldState`'s shared-task idempotency, keyed by slug:
  ```csharp
  public sealed class PublicWorldState(NornisApiClient api)
  {
      private string? _slug;
      private Task<ApiResult<PublicWorldDto>>? _task;
      public Task<ApiResult<PublicWorldDto>> GetAsync(string slug)
      {
          if (_slug != slug) { _slug = slug; _task = api.GetPublicWorldAsync(slug); }
          return _task!;
      }
  }
  ```
  Register `AddScoped` in `Program.cs:124-127`. This also removes the serialization: the page can start its own fetch and `await` the shared world task afterwards, so the two overlap.
- **Effort:** small
- **Risk:** The cache lives for the circuit's lifetime, so a GM who flips a world from public to private will not see it disappear from an already-open anonymous tab until reload. That is already true of the rendered page, so no regression.

---

### [SEVERITY: Medium] `/sources` polls the full source list every 4 seconds and never stops for a stuck source
- **Where:** `src/Nornis.Web/Components/Pages/Sources.razor:174-197` (esp. `:178` interval, `:181` predicate, `:185` fetch)
- **What:** A 4-second `PeriodicTimer` refetches `GetSourcesAsync(worldId, campaignFilter)` — the whole list, not a lightweight status endpoint — whenever any source is in `Ready`, `Queued`, or `Processing`. `"Ready"` is included in `ActiveStatuses` (`:137`), but `Ready` is a resting state a source sits in until the worker picks it up. If the worker is down, backed up, or the source failed to enqueue, the page polls the full list **15 times a minute indefinitely**.
  `SourceDetail.razor:646-695` has the same 4 s interval and the same `Ready` in its `ActiveStatuses`, though it fetches a single source rather than a list.
- **Why it costs:** 15 requests/minute per open `/sources` tab, each returning every source in the world with campaign joins. On a world with a large backlog this is the heaviest recurring query in the client. A GM who leaves `/sources` open during a session with one stuck note pays this for the whole session. Note the lighter `sources/activity` endpoint already exists (`NornisApiClient.cs:194`) and is described as "Lightweight activity counts" — the list poll could be gated on it.
- **Fix:** Three changes, cheapest first:
  1. Back off geometrically: 4 s for the first ~30 s, then 10 s, then 30 s, capping out. Extraction takes tens of seconds, so a fixed 4 s is far tighter than the thing it is watching.
  2. Poll `GetSourceActivityAsync` (cheap counts) and only refetch the full list when the counts actually move.
  3. Add a stop condition: after N consecutive ticks with no status change, drop to a slow poll or stop and show a manual Refresh.
- **Effort:** small
- **Risk:** Slower perceived movement of the status chips. Mitigated by (2), which keeps the *detection* fast and only makes the expensive fetch conditional.

---

### [SEVERITY: Medium] Long lists render every row, with filtering redone in property getters on each keystroke
- **Where:** `src/Nornis.Web/Components/Pages/Artifacts.razor` and `src/Nornis.Web/Components/Pages/Public/World/PublicWorldArtifacts.razor:152-165` (`Filtered`), `Components/Pages/Sources.razor:130-133` (`FilteredSources`), `Components/Shared/ProcessingQueuePanel.razor:95-106` (`Pending`, `StatusCounts`)
- **What:** No `<Virtualize>` anywhere in the project (verified by grep — zero occurrences). Every list page renders one DOM element per row for the whole result set. On top of that, the filter is a property getter that re-runs its LINQ and materializes a new list on each access, and it is accessed multiple times per render: in `PublicWorldArtifacts.razor` alone, `Filtered` is read at line 44 (`!Filtered.Any()`), line 73 (`Filtered.GroupBy`) or line 111 (`foreach`), and again from inside `FilteredGraph` at line 194. The search box that drives it is `Immediate="true"` with **no debounce** (`PublicWorldArtifacts.razor:28-30`, and the same pattern in `Artifacts.razor`), so every keystroke triggers a full re-render plus 2–3 complete sort-and-materialize passes over the artifact list.
- **Why it costs:** On Blazor Server both halves land on the server: the LINQ churn is server CPU/GC, and the render diff for a several-hundred-row list is pushed over the SignalR circuit on every keystroke. A codex with 500 artifacts means a 500-element diff computed and transmitted per character typed. `ProcessingQueuePanel.StatusCounts` is worse per-element: it runs a `Count` over all sources once per status (6 statuses) plus once more for Processed — 7 full scans per evaluation.
- **Fix:**
  1. Debounce the client-side search. MudBlazor supports it directly — `DebounceInterval="250"` on the `MudTextField` alongside `Immediate="true"` — a one-attribute change per search box.
  2. Memoize the filter result on `(source list, type, search, sort)` using the same cache-key pattern the file already uses for `FilteredGraph` (`PublicWorldArtifacts.razor:186-203`).
  3. Wrap the long lists in `<Virtualize Items="Filtered" Context="a">…</Virtualize>` and add `@key="a.Id"` to the row so Blazor can diff by identity. (`@key` is used correctly elsewhere — `SourceDetail.razor:291,300,346,367`, `MapViewer.razor:15` — just not on these lists.)
- **Effort:** small (1 and 2) / medium (3)
- **Risk:** `Virtualize` requires a fixed-ish row height to size its scroll spacer; the artifact **card grid** is a CSS grid with variable-height cards and will not virtualize cleanly — apply virtualization to the tree/list views and leave the card grid to the debounce + memoization fixes.

---

### [SEVERITY: Low] Global search debounce is tighter than it needs to be
- **Where:** `src/Nornis.Web/Components/Shared/GlobalSearch.razor:23` (`DebounceInterval="200"`)
- **What:** The top-bar autocomplete fires `SearchArtifactsAsync` 200 ms after the last keystroke, with `MinCharacters="2"`. It is mounted in `MainLayout.razor:31`, so it is live on every authenticated page.
- **Why it costs:** 200 ms is below normal inter-keystroke intervals for a fast typist, so a 12-character query can still fire 3–5 backend searches instead of 1. Each is an authenticated API call running a relevance search over the world's artifacts. Not large in absolute terms, but it is per-keystroke-burst on a control that is always present.
- **Fix:** Raise to `DebounceInterval="350"` and consider `MinCharacters="3"`. Both are attribute changes on the existing `MudAutocomplete`.
- **Effort:** trivial
- **Risk:** Very slightly later suggestions. 300–400 ms is the conventional range for search-as-you-type.

---

### [SEVERITY: Low] Google Fonts is a render-blocking third-party dependency on every page
- **Where:** `src/Nornis.Web/Components/App.razor:8-10`
- **What:** A render-blocking `<link rel="stylesheet">` to `fonts.googleapis.com` for Cormorant Garamond (3 weights) and Inter (4 weights), preceded by two `preconnect` hints. `display=swap` is set, which is correct.
- **Why it costs:** Two extra DNS+TLS handshakes and a blocking CSS fetch before first paint on every cold load, plus up to 7 woff2 downloads. It also means the app's first paint depends on a third party's availability, and it sends every visitor's IP and user-agent to Google — worth noting given the app ships a Privacy page.
- **Fix:** Self-host the two families. Drop the woff2 files into `wwwroot/fonts/`, add `@font-face` rules to `app.css`, and delete lines 8-10. With `MapStaticAssets` (see the compression finding) they get immutable caching for free, and they stop being a third-party request entirely. Subsetting to latin only would cut them further.
- **Effort:** small
- **Risk:** Must cover the same weights currently requested (500/600/700 and 400/500/600/700) or headings shift. Check the licence terms for redistribution — both are SIL OFL, which permits it.

---

## Verified clean — things checked that turned out fine

Worth recording so they are not re-audited:

- **JS interop disposal is consistently correct.** Every component that allocates a `DotNetObjectReference` or a JS-side instance disposes both, with a `JSDisconnectedException` catch: `ArtifactGraph.razor:161-172`, `NotesEditor.razor:134-144`, `StorylineTimelineChart.razor:280-294`, `JourneyMap.razor:378-392`, `MapViewer.razor:83-97`, `InkViewer.razor`, `Capture.razor:290`. The JS side matches — `nornis-graph.js:246-253` disconnects its `ResizeObserver` and calls the force-graph destructor; `nornis-ink.js:288` disconnects its observer; `nornis-journey.js`, `nornis-map-edit.js` and `nornis-timeline.js` each remove every `document`-level listener they add.
- **State-container and `LocationChanged` subscriptions are all unsubscribed.** Every component that does `Worlds.Changed +=` implements `IDisposable` and pairs it with a `-=`; the two `Nav.LocationChanged` subscribers (`NavMenu.razor:206`, `TutorialChecklist.razor:121`) both unsubscribe (`:336`, `:307`). No leaked handlers found.
- **`OnAfterRenderAsync` implementations are correctly `firstRender`-gated** where they should be (`NavMenu.razor:276-287`, `Profile.razor:392-401`, `Ask.razor:187-205`, `NotesEditor.razor:107-116`). The three that intentionally are not (`ArtifactGraph.razor:110`, `StorylineTimelineChart.razor:235`, `JourneyMap.razor:368`, `MapViewer.razor:60`) each guard on a `_pendingRender` / `_jsInitialized` / `_dragInitialized` flag so the interop runs once, not per render.
- **`WorldState` already de-duplicates its own fetches properly** — `EnsureLoadedAsync` (`WorldState.cs:102-110`) and `EnsureContinuityLoadedAsync` (`:206-214`) share one in-flight `Task` across all concurrent callers, and the continuity cache is keyed on world id. `GET /api/worlds` and the continuity assessment fire exactly once per circuit per world. This is the pattern the other findings recommend copying.
- **`Home.razor:395-400` and `CostsPanel.razor:258-261` already parallelize correctly** with `Task.WhenAll`. Home's dashboard is a five-call parallel fan-out, which is the right shape.
- **The TipTap editor has no auto-save**, so there is no debounce to get wrong — content is pulled once on save via `NotesEditor.GetMarkdownAsync` (`:119-129`).
- **`ReviewQueuePanel.OnParametersSetAsync` (`:178-190`) explicitly guards against a reload loop** and documents why, which is exactly the trap this pattern usually falls into.
- **The global tooltip handlers in `nornis-timeline.js:253-278`** attach `pointerover`/`pointermove`/`pointerdown`/`scroll` at document level on every page, but each early-returns unless a tip is currently open, so the per-event cost is a null check.
- **`wwwroot` images are already small** (largest 18.7 KB) — no image optimization work needed.

---

## Unverified / worth a look

Things I could not confirm by reading the client code alone:

1. **Whether MudTabs eagerly initializes all three Admin panels.** `Components/Pages/Admin.razor:26-37` puts `WorldSettingsPanel`, `CostsPanel` and `ProcessingQueuePanel` in three `MudTabPanel`s. If MudBlazor 7.15 renders only the active panel (the documented default, `KeepPanelsAlive="false"`), opening `/admin` costs the Settings panel's 5 requests. If it renders all three, it costs ~11 requests on open, and switching tabs re-runs each panel's `OnInitializedAsync` every time. The MudBlazor assembly is compiled so I could not read the behaviour; worth confirming in the browser network tab.
2. **Whether the Container Apps ingress already gzips responses.** If Envoy is configured to compress, the "uncompressed static assets" finding shrinks to just the missing immutable caching/fingerprinting — still worth fixing, but not a 1 MB win. Check a `curl -H 'Accept-Encoding: gzip' -I` against the deployed `app.css`.
3. **Whether `PollActivityAsync` / `PollWhileProcessingAsync` timers started during the prerender pass are actually cancelled.** `Dispose` cancels the CTS, and the SSR renderer should dispose its components at end of response, but I did not verify the runtime behaviour. If they are not disposed, every full page load leaks a 15 s and a 4 s timer for the life of the request scope. Easy to check with a counter in `RefreshActivityAsync`.
4. **Actual artifact/source list sizes in production.** The virtualization and memoization findings scale with list length; if the largest real world has 60 artifacts, finding 10 drops to Low. If it has 2,000, it moves up.
5. **`app.css` is 3,858 lines and unminified.** I did not audit it for dead rules. Given that it compresses 91 KB → 17 KB, minification would add little once compression is on, but there may be unused selectors worth pruning.
6. **`GetAskSuggestionsAsync` is fetched by both `Home.razor:399` and `Ask.razor:221`.** These are separate pages so it is not a same-page duplicate, but the Home hero's primary action navigates straight to `/ask`, which immediately refetches the identical suggestion list. A shared cache would save one call on the app's most common flow — I did not confirm the endpoint is expensive enough to matter.
