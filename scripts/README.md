# Scripts

## provision-azure.ps1

Provisions the Azure environment (Container Apps, ACR, SQL, Service Bus). See
`.kiro/steering/azure-hosting.md` for the intended architecture and the deliberate
amendments made during provisioning.

## start-local.ps1 + compose.local.yaml

Full local stack: SQL Server and the Azure Service Bus emulator in Docker
(`compose.local.yaml`, queue defined in `servicebus-emulator.json`), migrations applied,
then API/Worker/Web launched with connection strings pointed at the containers —
overriding any cloud values in user secrets, so local runs never touch cloud SQL or
compete with the cloud worker for messages. Only the Azure OpenAI call leaves the
machine (key from user secrets). `-InfraOnly` starts containers + migrations without
launching apps.

```powershell
./scripts/start-local.ps1            # everything
./scripts/start-local.ps1 -InfraOnly # just SQL + Service Bus + migrations
docker compose -f compose.local.yaml down   # tear down
```

## coverage.ps1

Runs the test projects with coverage collection (`coverlet.runsettings` at the repo
root) and merges the results into a browsable HTML report under `artifacts/coverage/`
(gitignored). ReportGenerator is pinned in `.config/dotnet-tools.json`, so local and CI
runs share one version.

```powershell
./scripts/coverage.ps1                            # everything, opens the report
./scripts/coverage.ps1 -Projects Domain,Application
./scripts/coverage.ps1 -NoOpen
```

Projects are run one at a time, so a test binary locked by a running dev server is
reported as skipped instead of taking the whole run down. There is no coverage gate —
the report is a signal to read, not a bar to clear (`.kiro/steering/testing-strategy.md`,
`docs/plans/test-quality.md`).

## crap-report.ps1

Ranks methods by CRAP score — complexity squared against uncovered-ness cubed — so the
code that is dangerous to change sorts to the top. Reads the merged Cobertura that
`coverage.ps1` produces, writes `artifacts/coverage/crap.json` and `crap.md`, and prints
the worst fifteen.

```powershell
./scripts/coverage.ps1 -NoOpen   # produces the merged Cobertura
./scripts/crap-report.ps1        # ranks it
./scripts/crap-report.ps1 -Top 50
```

The list is a test-writing backlog ordered by risk, never a gate. Razor `BuildRenderTree`
methods are excluded — compiler-generated markup that would otherwise fill the table and
bury everything a person could act on.

## import-notes.py

Bulk-imports a note-vault export (wiki-style folder layout: `Wiki/`, `Characters/`,
`Campaigns/<n> - <name>/Arc .../<date>/...`) into a Nornis world as `ImportedNote`
sources, creating campaigns from folder structure and auto-accepting each source's
proposals before the next is extracted so the record consolidates as it builds.

```
python scripts/import-notes.py --root "path/to/Export/Vault" --world-id <guid> \
    --base-url https://<nornis-api> [--dry-run]
```

Before a large run:

- raise `AiBudget__DailyWorldBudgetUsd` on the API and worker container apps (a ~2 MB
  vault costs roughly $5–6 of AI usage), and restore it afterward;
- expect Azure OpenAI content-filter rejections on the occasional grisly session note —
  those sources stay `Failed` with raw text preserved; soften and re-run to include them;
- the run is resumable (already-imported titles are skipped) and ends with a sweep that
  re-attempts deferred proposals; anything still pending needs manual review in the UI.

First used for the Ruins of Symbaroum import (2026-07): 83 sources → 360 artifacts,
1,055 facts, 345 relationships. See `docs/features/9-worlds-and-campaigns/`.
