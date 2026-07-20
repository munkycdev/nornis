<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/transparent-logo.png">
    <img src="docs/images/mini-logo.png" alt="Nornis" height="72">
  </picture>
</p>

<h1 align="center">Nornis</h1>

<p align="center"><em>It's your epic. Every source leaves a mark.</em></p>

Nornis is a world memory engine for tabletop roleplaying games.

You feed it the raw material of your game — session notes, handwritten pages, uploads,
images, maps — and it reads that material and *proposes* structured knowledge: characters,
locations, factions, items, events, storylines, and the facts and relationships that connect
them. Nothing enters the record without a human accepting it.

What accumulates is a searchable, cited record of your world that you can ask questions of in
plain language, browse as a codex or a graph, walk along a timeline, trace across a map, and
selectively share with your players.

**Nornis is not a wiki.** Sources are raw material; artifacts are memory. The product is the
transformation between them.

---

## The loop

```text
Capture a source
      ↓
Async AI extraction
      ↓
Review proposals  ──→  Accept / Edit / Reject
      ↓
Artifacts, facts, and relationships update
      ↓
Ask the Loremaster
```

### What that looks like

You paste a session note:

> We questioned Captain Voss in Black Harbor. He denied knowing about the missing caravan,
> but Tavrin found the Silver Key in his quarters.

Nornis proposes — and waits for you to decide on each one:

- Create or update `Captain Voss`, and connect him to `Black Harbor`
- Add the claim *"Captain Voss denied knowing about the missing caravan"*
- Create or update `Silver Key`, with the fact *"found in Voss's quarters"*
- Open the storyline `Missing Caravan`, linking Voss, Black Harbor, and the Silver Key

Accept, edit, or reject each proposal. What you accept becomes canon, and every accepted
claim keeps a link back to the sentence in the source that produced it.

---

## What it does

| Area | What it gives you |
|---|---|
| **Capture** | Session notes, GM prep, handwritten pages (transcribed), an in-app ink canvas, images (vision-read), PDFs and documents, maps (place names and pins read off the image), and links. |
| **Review** | Every AI suggestion lands in a queue with its confidence, its source, and its rationale. Accept, edit the values first, or reject — individually or in dependency-safe bulk. |
| **Codex** | Browse everything the world knows as cards, a collapsible tree, or a live force-directed graph. Every artifact shows its facts, truth states, relationships, open questions, and source excerpts. |
| **Ask the Loremaster** | Ask your world questions in plain language. Answers are grounded strictly in your accepted record plus your indexed library, every claim cited, with a confidence rating — and it says so when it doesn't know. |
| **Timeline & journey** | Storylines laid out over the real session calendar, and the party's trail across a map walked session by session. |
| **Secrets & reveals** | Everything carries a scope — Private, GM only, or Party visible. When the fiction discloses a secret, the GM ticks exactly what the party now learns; Nornis checks the reveal leaves no dangling references. One-way, by design. |
| **Library** | Upload sourcebooks, maps, and handouts. PDFs are indexed into passages so the Loremaster can quote them with page citations. |
| **Continuity health** | A read on how coherent the record is — contradictions, dangling threads, stale storylines — each finding with a severity and a jump to the artifact. |
| **Sharing** | Invite players by link with a role. Optionally give the world a public address for a read-only, party-visible-only view. |
| **Cost visibility** | Every AI call is metered by operation, model, and user, against a per-world daily budget. |

Roles are **GM**, **Player**, and **Observer** — and they see genuinely different worlds. A
player and a GM asking the Loremaster the same question get different answers, because
retrieval respects visibility.

Per-feature design docs are in [`docs/features/`](docs/features), numbered in build order.

---

## Architecture

Three independently deployable services over a shared Clean Architecture core:

```text
nornis-web      Blazor Web App (MudBlazor) — the UI
nornis-api      ASP.NET Core — the HTTP API
nornis-worker   Background processor — drains the extraction queue
```

| Project | Role |
|---|---|
| `src/Nornis.Domain` | Entities, enums, repository interfaces. No EF Core, no Azure, no UI. |
| `src/Nornis.Application` | Use cases, services, authorization, AI orchestration. Depends on interfaces only. |
| `src/Nornis.Infrastructure` | EF Core repositories, migrations, blob storage, Service Bus, Azure OpenAI. |
| `src/Nornis.Api` | Controllers, request/response contracts, auth filters. |
| `src/Nornis.Web` | Blazor pages and components, API client. |
| `src/Nornis.Worker` | Queue-driven extraction, indexing, and transcription jobs. |
| `src/Nornis.Shared` | Types shared across hosts. |

**Stack:** .NET 10 · Blazor · ASP.NET Core · Azure SQL (EF Core, repository pattern) ·
Azure Blob Storage · Azure Service Bus · Azure OpenAI · Auth0 (Discord) · Azure Container
Apps · GitHub Actions.

Every project has a matching test project under [`tests/`](tests); NUnit throughout.

---

## Running locally

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (see
[`global.json`](global.json)), Docker Desktop, PowerShell 7+.

The local stack runs SQL Server and the Azure Service Bus emulator in Docker, applies
migrations, and launches all three apps with connection strings pointed at the containers —
so a local run can never touch cloud SQL or compete with the cloud worker for queue messages.

```powershell
# Everything: containers, migrations, API + Worker + Web
./scripts/start-local.ps1

# Just SQL + Service Bus + migrations
./scripts/start-local.ps1 -InfraOnly

# Tear down
docker compose -f compose.local.yaml down
```

- Web: <http://localhost:5100>
- API: <http://localhost:5000> (dev-auth bypass active — no Auth0 round trip)

The only calls that leave your machine are to Azure OpenAI, so the AI does real work. The API
and Worker have separate user-secret stores and separate deployments — extraction runs in the
Worker, everything conversational runs in the API:

```powershell
# Worker — extraction (deployment: nornis-extract)
dotnet user-secrets --project src/Nornis.Worker set "Extraction:AiEndpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets --project src/Nornis.Worker set "Extraction:AiApiKey"  "<your-key>"

# API — Ask the Loremaster, continuity health, retrospectives, library
# retrieval (deployment: nornis-ask)
dotnet user-secrets --project src/Nornis.Api set "Loremaster:AiEndpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets --project src/Nornis.Api set "Loremaster:AiKey"      "<your-key>"
```

Neither is required to boot. Without them the app runs fine — capture, browse, review, share —
and the AI paths fail with an explicit "not configured" message rather than a crash.

> The `AiModel` values in `appsettings.json` are Azure OpenAI **deployment** names, not model
> names, and the `ModelPricing` key must match, or cost tracking silently records $0.

### Build and test

```powershell
dotnet build Nornis.sln
dotnet test Nornis.sln

# One project
dotnet test tests/Nornis.Application.Tests/

# One fixture
dotnet test tests/Nornis.Application.Tests/ --filter "FullyQualifiedName~LibraryServiceTests"
```

Warnings are errors ([`Directory.Build.props`](Directory.Build.props)), so a clean build is
the bar.

---

## Deployment

Pushing to `main` deploys. [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)
builds the three images in parallel with the test run, applies EF migrations, then rolls the
Azure Container Apps forward — gated on tests passing, so images for a failing commit are
tagged but never deployed. Pull requests run
[`ci.yml`](.github/workflows/ci.yml) instead: restore, build, test, format.

Because migrations run *before* the new images go live and the old revision keeps serving
until the rollout, **migrations must stay additive**.

Infrastructure is provisioned by [`scripts/provision-azure.ps1`](scripts/provision-azure.ps1).

---

## Repository layout

```text
src/            Application source (see the table above)
tests/          One test project per source project
docs/features/  Per-feature design docs, numbered in build order
docs/           Backlog and images
scripts/        Local stack, Azure provisioning, bulk note import
.kiro/steering/ Product vision, architecture, and standards that guide the build
```

Two documents are worth reading before changing anything:
[`.kiro/steering/product-vision.md`](.kiro/steering/product-vision.md) for what Nornis is
trying to be, and [`.kiro/steering/coding-standards.md`](.kiro/steering/coding-standards.md)
for how it's built.

---

## Conventions

- Domain vocabulary is deliberate: **Storyline** (never "Thread"), **Source** (never
  "Evidence"), **Artifact**, **Fact**, **Relationship**, **Canon**, **Reveal**.
- Repository pattern over EF Core — application services never touch `DbContext`.
- Authorization is enforced server-side, in application services.
- AI proposes; a human decides. Nothing mutates canon on its own.

---

## License

[MIT](LICENSE)
