# Scripts

## provision-azure.ps1

Provisions the Azure environment (Container Apps, ACR, SQL, Service Bus). See
`.kiro/steering/azure-hosting.md` for the intended architecture and the deliberate
amendments made during provisioning.

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
