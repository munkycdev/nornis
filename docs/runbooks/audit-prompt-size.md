# Continuity Audit prompt outgrowing the model

## Symptom

`nornis-audit-prompt-size` fires: a Continuity Audit prompt exceeded 100k input tokens.

Severity 3, and it is a design signal rather than an incident. Nothing is down. The audit
reads a world's whole record to look for contradictions, so its prompt grows with the
world — and this alert is the point at which that growth stops being free.

Two things follow, in this order:

1. **Cost.** Input tokens are billed. A 100k-token prompt run hourly is real money.
2. **Correctness.** Approaching the context window, the model starts losing the middle of
   the record. The audit does not fail; it quietly gets worse, which is harder to notice.

## Diagnose

How large, how fast, and which world:

```bash
az monitor app-insights query --app appi-nornis -g rg-nornis --offset 7d --analytics-query "
customMetrics
| where name == 'nornis.ai.input_tokens'
| where tostring(customDimensions['operation_type']) == 'ContinuityAudit'
| summarize MaxTokens = max(valueMax), Runs = sum(itemCount) by bin(timestamp, 1d)
| order by timestamp asc" -o table
```

A steady climb is the expected shape — the world is growing. A step change means something
else: a bulk import landed, or the audit's retrieval stopped bounding what it pulls in.

Cross-check against spend on the Costs page in the app, filtered to the ContinuityAudit
operation type.

## Remedy

There is no emergency action, and no fix that belongs in a runbook — the remedies are
product decisions:

- **Cap the record the audit reads**, prioritising recent and canon material over
  everything ever written.
- **Chunk the audit**, running it over slices of the world and merging findings.
- **Reduce cadence.** The audit runs hourly via `ContinuityAuditBackgroundService`; for a
  large, slow-moving world that is far more often than the record changes.

The cadence lever is the only one available without code:

```bash
az containerapp show -n ca-nornis-api -g rg-nornis \
  --query "properties.template.containers[0].env[?starts_with(name,'ContinuityAudit')]" -o table
```

If the alert is firing repeatedly and the work is not scheduled, raise it as a plan item
rather than treating it as an incident. `docs/plans/` is where that belongs.

## Verify

Nothing to verify — this alert reports a trend, not a fault. It will keep firing until the
prompt shrinks, which needs one of the changes above. If you silence it, write down why and
when to look again; an alert quietly muted is worse than one that never existed.
