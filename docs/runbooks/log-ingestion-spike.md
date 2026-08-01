# Log ingestion spike

## Symptom

`nornis-log-ingestion-spike` fires: *"Billable log ingestion exceeded 200 MB in 6h — a log
flood is underway; the 0.5 GB daily cap will trip soon."*

Nothing is broken. This is a cost alert, and it is early by design: it warns while there is
still room under the daily cap. If the cap does trip, App Insights stops ingesting for the
rest of the day and you lose the telemetry you would want for whatever comes next.

## Diagnose

Find what is flooding:

```bash
az monitor app-insights query --app appi-nornis -g rg-nornis --offset 6h --analytics-query "
union *
| where timestamp > ago(6h)
| summarize Records = sum(itemCount), MB = round(sum(_BilledSize) / 1024.0 / 1024.0, 1) by \$table, cloud_RoleName
| order by MB desc
| take 15" -o table
```

Weight by `itemCount`: API telemetry is sampled at 10%, so raw row counts understate its
true volume roughly tenfold. The worker is unsampled — deliberately, since its volume
scales with queue depth rather than with open browser tabs — so its numbers are literal.

| Dominant table | Likely cause |
| --- | --- |
| `traces` | A logger left at Debug/Information in a hot path |
| `dependencies` | A retry storm, or a bulk import driving thousands of SQL and AI calls |
| `exceptions` | Something failing repeatedly — the flood is a symptom, find the fault |
| `requests` | A polling loop, or genuine traffic |

If `exceptions` dominates, this is the wrong runbook. The flood is telling you about the
underlying failure — go and fix that.

## Remedy

**A bulk import is running.** Expected. `scripts/import-notes.py` drives thousands of
operations, and the alert is doing its job by saying so. Let it finish; consider raising
the daily cap for the duration if the import is long.

**A logger is too chatty after a deploy.** Roll back or lower the level. Sampling is
already the main control:

```bash
# API defaults to 0.10; the worker to 1.0. Tighten temporarily if needed.
az containerapp update -n ca-nornis-api -g rg-nornis \
  --set-env-vars "Telemetry__SamplingRatio=0.05" -o none
```

Failed requests are kept regardless of ratio, so tightening sampling costs successful-request
detail rather than incident debuggability.

**A retry storm.** Fix the cause; the volume follows.

## Verify

Re-run the KQL over the last hour and confirm the dominant table has dropped back. The
alert resolves on its own once the 6h window rolls past the spike.

## Notes

Put `Telemetry__SamplingRatio` back when the incident is over. A permanently tightened
ratio is a silent loss of diagnostic detail that nobody notices until the next outage —
and any KQL that counts raw rows must weight by `itemCount` to stay honest under sampling.
