# Operational hardening

> Part of the Nornis backlog. This file is a spec, not authorization: execute only
> through the Execution order in `docs/future-features.md`, which holds sequencing,
> completion status, and the Opus/Fable gate.

2026-07-31. Six items from an ops review of the deploy pipeline, messaging paths, and
credential setup. The theme the review surfaced: Nornis is well-instrumented for "is
it working" and thin on "what happens when it isn't." Items are independent and
ordered by priority; O1 and O2 interlock with the System status plan above.

Considered and deferred, not rejected: a rehearsed restore drill plus world
soft-delete, Azure-side cost tripwires, and local-dev/prod separation.

## O1 — post-deploy verification and revision safety

`deploy.yml` currently ends the moment `az containerapp update` returns; nothing
confirms the new revision came up.

- Wire `/health` as Container Apps liveness + readiness probes on `ca-nornis-api`
  (and on Web once the status plan gives it a `/health`), so a failing revision
  never takes traffic and the old revision keeps serving.
- Pipeline step after the update: poll the public `/health` until healthy, with a
  timeout that fails the run loudly. The response body names the failing check, so
  the step can distinguish "app broken" from the known pending-migrations window and
  say "run the manual migration step" instead of a bare failure.
- The Worker has no probe surface; its post-deploy verification is the
  worker-heartbeat check in the System status plan.
- `containerapp update` kills mid-extraction work. Safety rests on Service Bus
  redelivery plus the idempotency items in the defect plan below — this raises
  their priority.

## O2 — dead-letter queue visibility

`RedeliveryBackoff` deliberately preserves the dead-letter backstop; nothing watches
the backstop. A message that exhausts retries today vanishes silently.

- Azure Monitor alert on dead-lettered message count > 0.
- A `dlq` row in the `/status` `deps` checks — message count only, via the
  admin client.
- `scripts/dlq.ps1`: peek, resubmit, purge — the runbook companion for O6.

## O3 — managed identity sweep

SQL, blob, and Service Bus all authenticate by connection string in config. The
deploy pipeline already uses OIDC; extend the pattern to runtime.

- Blob and Service Bus first (easy): `DefaultAzureCredential` + endpoint, with
  Storage Blob Data Contributor and Service Bus sender/receiver roles on each app's
  identity. SQL after: Entra auth with contained users provisioned per identity.
- Config becomes endpoints, not secrets; the startup guards that demand connection
  strings change wording to demand endpoints.
- Local dev keeps working via `DefaultAzureCredential`'s az-login fallback against
  the same resources — unchanged until dev/prod separation is taken up.

## O4 — AI kill switch

Per-world budgets cap spend; nothing can pause all paid AI during a provider
incident or runaway behavior without a redeploy.

- One global flag in a new single-row operational-flags table (additive migration),
  read with a ~60s cache at every paid-AI dispatch seam: extraction, Ask, continuity
  assessment, library indexing. Flipped by script; no admin UI needed yet.
- **Paused must not mean dead-lettered:** the Worker re-schedules messages using the
  same scheduled-copy mechanism `RedeliveryBackoff` already uses, so queued work
  waits out the pause without burning delivery counts. Interactive paths (Ask,
  assess) return an explicit "AI is paused" error.
- The status page renders the flag as a banner when set — a pause should look
  deliberate, not broken.

## O5 — dependency patching

- Dependabot: `nuget` + `github-actions` ecosystems, weekly, minor/patch grouped
  into one PR.
- `dotnet list package --vulnerable --include-transitive` as a CI step that fails
  on findings. The existing gates (warnings-as-errors, tests, format) are what make
  auto-bump PRs safe to merge quickly.

## O6 — runbooks

`docs/runbooks/`, one short doc per nameable failure mode: worker dead, migration
missed, DLQ non-empty, Auth0 outage, budget cap hit, AI paused. Each: the symptom
(which alert fires, what `/status` shows), diagnosis steps, remedy commands,
verification. Every Azure alert's description links to its runbook — an alert that
doesn't say what to do next is half an alert.
