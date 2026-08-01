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

- ~~Wire `/health` as Container Apps liveness + readiness probes on `ca-nornis-api`
  (and on Web once the status plan gives it a `/health`), so a failing revision
  never takes traffic and the old revision keeps serving.~~ **Done 2026-08-01, with
  liveness pointed somewhere else on purpose.**
  - **Readiness → `/health`.** This is what the stated goal actually describes: a
    revision whose `/health` never goes green never becomes ready, so traffic stays on
    the previous one. `initialDelaySeconds 10`, `failureThreshold 6`, `timeout 10` —
    generous, because a cold start against an idle database has been measured at eight
    seconds and a probe that gives up sooner would fail every deploy.
  - **Liveness → TCP on 8080, not `/health`.** Liveness restarts the container, and
    `/health` returns 503 while a migration is pending. Wiring the two together turns
    the documented migration window into a crash loop: restart, migration still
    pending, restart again — replacing a revision that serves 503 and says what is
    wrong with one that never stays up long enough to say anything. TCP answers the
    question liveness is actually asking, which is whether the process is listening.
  - Applied as an ARM `PATCH` on `properties.template` alone, so
    `properties.configuration` — where the secrets live — was never in the request.
    Verified after: env var names, secret names and image identical on both apps.
    `az containerapp update --yaml` would have round-tripped the whole app including
    secret placeholders, which is the way this goes wrong.
  - Two API-version potholes on the way: the captured template carries a read-only
    `imageType` the PATCH validator rejects, and `scale.cooldownPeriod` /
    `pollingInterval` are unknown to `2024-03-01`. Strip the first, use `2025-01-01`
    for the second. Both failures were clean — nothing was applied.
- ~~Pipeline step after the update: poll the public `/health` until healthy, with a
  timeout that fails the run loudly.~~ **Done 2026-08-01** — `deploy.yml` now polls
  `/health` for up to three minutes and fails the run loudly, then warms the new
  revision (api and web run at `minReplicas=1`, so a revision swap is the only moment
  they are ever cold) and prints `/status` into the step summary. It gates on `/health`
  only: `/status` covers dependencies the deploy does not control, and failing a
  release because Service Bus is having a moment would train everyone to ignore a red
  pipeline.
  - **Still open:** the spec assumed "the response body names the failing check, so
    the step can distinguish 'app broken' from the known pending-migrations window."
    It does not — `/health`'s writer emits `{status}` and nothing else, deliberately.
    Naming the failing check there is an additive change to a body the availability
    alert reads, so it belongs to whoever finishes O1, not to a drive-by.
- The Worker has no probe surface; its post-deploy verification is the
  worker-heartbeat check in the System status plan.
- `containerapp update` kills mid-extraction work. Safety rests on Service Bus
  redelivery plus the idempotency items in the defect plan below — this raises
  their priority.

## O2 — dead-letter queue visibility

`RedeliveryBackoff` deliberately preserves the dead-letter backstop; nothing watches
the backstop. A message that exhausts retries today vanishes silently.

**Done 2026-08-01**, with one bullet deliberately dropped.

- ~~Azure Monitor alert on dead-lettered message count > 0.~~ Already existed:
  `nornis-sb-deadletter`, `DeadletteredMessages > 0`, evaluated every 5 minutes.
- ~~A `dlq` row in the `/status` `deps` checks — message count only, via the admin
  client.~~ **Not built, and should not be.** Reading queue depth needs Manage on the
  namespace. The namespace has deliberate least-privilege policies — `nornis-send` for the
  API, `nornis-listen` for the worker, `nornis-manage` for the KEDA scaler alone — so the
  only way to put that number on the page is to hand the most exposed component in the
  system queue administration. The alert already detects it, and the count is an
  operator's question rather than a public one. This is the same wall the `service-bus`
  check hit; there the answer was to ask a question the API had rights to ask, and here
  there is no such question.
- ~~`scripts/dlq.ps1`: peek, resubmit, purge — the runbook companion for O6.~~ Built,
  speaking the Service Bus REST API over a SAS token rather than loading the .NET SDK, so
  it needs pwsh and nothing else. Credentials come from the `sb-manage` secret through the
  operator's own `az login`; nothing in the running system gains access.
  - Verified against a throwaway queue seeded with a real dead-lettered message, which is
    the only way any of it could be verified — production had no dead letters. That test
    caught a genuine bug: peek unlocked each message as it read it, so the next request
    returned the same one and a single stuck message reported as ten. Peek now holds its
    locks through the walk and releases them in a `finally`.

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

**Done 2026-08-01.**

- ~~Dependabot: `nuget` + `github-actions` ecosystems, weekly, minor/patch grouped
  into one PR.~~ `.github/dependabot.yml`. Majors stay ungrouped on purpose — they
  change behaviour and each wants its own read and its own revert. The `github-actions`
  ecosystem is also what will propose the Node 20 major bumps the run annotations have
  been asking for.
- ~~`dotnet list package --vulnerable --include-transitive` as a CI step that fails
  on findings.~~ In `ci.yml`. Two things worth knowing about it: the command **exits 0
  even when it finds vulnerabilities**, so the step greps its output rather than
  trusting the exit code; and it is belt to NuGet Audit's braces rather than the only
  protection — restore already fails the build on a vulnerable package, because
  NU1901-NU1904 are errors under `TreatWarningsAsErrors`. That is why
  `Nornis.Infrastructure` and `Nornis.Web` carry explicit pins past advisories. The
  step's value is a legible report, and surviving audit ever being narrowed.

## O6 — runbooks

**Done 2026-08-01.** `docs/runbooks/` — nine docs, and every one of the six Azure
alerts in `rg-nornis` now carries a `Runbook:` link in its own description.

Written against what exists rather than the spec's list verbatim:

- The list named **AI paused**; there is no kill switch yet (O4), and a runbook for a
  control that does not exist is worse than none. Recorded as pending in the index.
- Three alerts had no entry on the list and needed one anyway — `nornis-sql-dtu`,
  `nornis-log-ingestion-spike`, `nornis-audit-prompt-size` — because "every alert links
  to its runbook" is the harder half of this item and those three are alerts.
- Two runbooks deliberately end without an incident action. Audit prompt size reports a
  trend whose remedies are product decisions, and budget cap hit is the system doing
  what it was told. Saying so is more useful than inventing a command.
