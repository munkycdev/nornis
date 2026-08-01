# Database under pressure

## Symptom

`nornis-sql-dtu` fires: *"nornis-db sustained above 80% DTU (Basic tier, 5 DTU)."*

In the app this reads as everything being slow rather than anything being broken. On
`/status`, `sql` and `worker-heartbeat` durations climb; past 15 seconds a probe times out
and the check flips to Unhealthy, which looks like an outage but is congestion.

Five DTUs is a very small database. It does not take much.

## Diagnose

```bash
az monitor metrics list --resource "$(az sql db show -g rg-nornis \
  -s sql-chronicis-dev -n nornis-db --query id -o tsv)" \
  --metric dtu_consumption_percent --interval PT1M --offset 1h -o table
```

Then find what is doing it:

```bash
az monitor app-insights query --app appi-nornis -g rg-nornis --offset 1h --analytics-query "
dependencies
| where type has 'SQL'
| summarize calls = sum(itemCount), p95 = percentile(duration, 95) by operation_Name
| order by p95 desc
| take 15" -o table
```

Weight by `itemCount`, not row count — API telemetry is sampled at 10%, so raw counts
understate by roughly ten times.

Usual causes:

| Cause | Tell |
| --- | --- |
| Bulk import running | `scripts/import-notes.py` in flight; sustained high load with an obvious start |
| Extraction burst | Worker at 1 replica chewing a large queue |
| A query that lost its index | One `operation_Name` dominating p95 with no change in traffic |
| Shared server contention | `sql-chronicis-dev` also serves Chronicis — the load may not be ours |

That last one matters and is easy to forget: the server is shared, so DTU pressure can
originate outside Nornis entirely.

## Remedy

**If an import or a large extraction is running:** let it finish. This is the expected
cost of the work, and the alert is telling you it is happening, not that it is wrong.

**If nothing should be running:** find the dominant query from the KQL above. A regression
usually traces to a recent deploy; rolling the API back is the fastest way to confirm.

```bash
az containerapp revision list -n ca-nornis-api -g rg-nornis -o table
az containerapp revision activate -n ca-nornis-api -g rg-nornis --revision <previous>
```

**If it is sustained and legitimate,** the tier is the problem, not the query. Scaling up
is a real cost decision, not an incident action — take it deliberately rather than at two
in the morning.

## Verify

```bash
curl -s -w "\ntotal=%{time_total}s\n" https://api.nornis.app/status
```

Warm, every check should answer in well under a second. Durations in the seconds mean
pressure has not cleared — but do not judge on the first request after an idle spell,
which pays cold-start cost and can measure several seconds on a perfectly healthy system.
Ask twice.

## Notes

`/health` stays 200 throughout. It only reports pending migrations, so a slow or loaded
database does not make the availability alert fire — deliberately, so congestion never
impersonates a broken deploy.
