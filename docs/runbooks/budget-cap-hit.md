# Budget cap hit

## Symptom

No alert fires. This one arrives as a user saying extraction stopped working, or as
sources refusing to process while the worker is plainly alive and the queue is draining.

`/status` will be entirely green: nothing is broken. The system is declining to spend
money, which is what it was told to do.

## How the cap works

`AiBudgetGuard` checks, before each AI call, how much that **world** has spent **today**
(UTC):

- A world's own `DailyAiBudgetUsd` wins if set.
- Otherwise the `AiBudget__DailyWorldBudgetUsd` environment variable applies. It is set on
  both `ca-nornis-api` and `ca-nornis-worker`, and both must agree — the API guards
  Loremaster and audit calls, the worker guards extraction.

**A cap of `0` means unlimited, not blocked.** The guard treats any non-positive budget as
"no budget configured" and allows the call. If your instinct during a runaway spend is to
set the cap to zero, you would be removing the brake. Set it to a small positive number
instead.

## Diagnose

```bash
# What is configured, on both hosts
for app in ca-nornis-api ca-nornis-worker; do
  echo "$app: $(az containerapp show -n $app -g rg-nornis \
    --query "properties.template.containers[0].env[?name=='AiBudget__DailyWorldBudgetUsd'].value | [0]" -o tsv)"
done
```

What has actually been spent is in the Costs page in the app (per world, per day), which
reads the same usage ledger the guard does. That is the authoritative number — it is
written by `AiUsageRecorder` on every call, successful or not.

If spend looks far below the cap and calls are still refused, the guard is not your
problem. Check [ai-call-failures.md](ai-call-failures.md) instead.

## Remedy

Raise the cap on **both** hosts — raising it on one leaves the other still refusing:

```bash
for app in ca-nornis-api ca-nornis-worker; do
  az containerapp update -n $app -g rg-nornis \
    --set-env-vars "AiBudget__DailyWorldBudgetUsd=10.0" -o none
done
```

`--set-env-vars` merges; it does not replace the other variables. Verify that anyway
before walking away — these apps hold connection strings.

For a single world rather than the whole system, set that world's `DailyAiBudgetUsd`
instead and leave the default alone.

**Put it back afterwards.** A raised cap is the failure mode that costs real money. The
bulk-import runbook in `scripts/README.md` says the same thing for the same reason: a
~2 MB vault import runs to roughly $5–6.

## Verify

```bash
for app in ca-nornis-api ca-nornis-worker; do
  az containerapp show -n $app -g rg-nornis \
    --query "properties.template.containers[0].env[?name=='AiBudget__DailyWorldBudgetUsd'].value | [0]" -o tsv
done
```

Then re-run the extraction that was refused and confirm it completes. The cap resets at
UTC midnight regardless — if the work can wait, waiting costs nothing and risks nothing.
