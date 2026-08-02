# Runbooks

One doc per nameable failure mode. Each answers the same four questions in the same
order: what you saw, how to confirm it, what to do, and how to know it worked.

The test of a runbook is whether someone who did not build the system can follow it at
two in the morning. If a step says "investigate", it is not finished.

| Runbook | Fires as |
| --- | --- |
| [Worker dead](worker-dead.md) | `/status` → `worker-heartbeat` Unhealthy; sources stuck Queued |
| [Migration missed](migration-missed.md) | The deploy run's verify step; `/health` 503 naming `pending-migrations` |
| [Dead-letter queue non-empty](dead-letter-queue.md) | `nornis-sb-deadletter` |
| [AI call failures](ai-call-failures.md) | `nornis-ai-call-failures`; `/status` → `azure-openai` Degraded |
| [Budget cap hit](budget-cap-hit.md) | No alert — surfaces as users reporting extraction refusals |
| [Database under pressure](database-pressure.md) | `nornis-sql-dtu`; `/status` slow or `sql` Unhealthy |
| [Auth0 outage](auth0-outage.md) | Nobody can sign in; `/status` stays green |
| [Log ingestion spike](log-ingestion-spike.md) | `nornis-log-ingestion-spike` |
| [Audit prompt size](audit-prompt-size.md) | `nornis-audit-prompt-size` |

Every Azure alert in `rg-nornis` links to its runbook from its own description, so the
notification itself carries the next step. If you add an alert, add its runbook and the
link in the same change — an alert that does not say what to do next is half an alert.

## Standing facts

Everything lives in resource group `rg-nornis`:

| | |
| --- | --- |
| API | `ca-nornis-api` → <https://api.nornis.app> |
| Web | `ca-nornis-web` → <https://nornis.app> |
| Worker | `ca-nornis-worker` — no ingress, `minReplicas 0`, wakes on queue depth |
| Service Bus | `sb-nornis-dev`, queues `source-extraction` and `library-indexing` |
| Database | `nornis-db` on `sql-chronicis-dev.database.windows.net` (Basic, 5 DTU) |
| Telemetry | `appi-nornis` |
| Status | <https://status.nornis.app> (GitHub Pages — outside Azure on purpose) |

Two endpoints, two meanings, and it matters which one you are reading:

- **`/health`** answers *is this deploy broken*. Only the pending-migrations check
  reports here, and the body names it when it fails. Read by the Container Apps
  readiness probe and by `deploy.yml`. **Nothing alerts on it** — `nornis-availability`
  watches a ping against the Web app's `/welcome`, which is green while the API is down.
- **`/status`** answers *are the dependencies healthy*. Five checks, anonymous, no
  detail beyond names and verdicts.

## Not yet written

- **AI paused** — waits on the kill switch (O4 in `docs/plans/operational-hardening.md`).
  There is nothing to pause yet, and a runbook for a control that does not exist would
  be worse than none.
