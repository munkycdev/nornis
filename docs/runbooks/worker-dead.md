# Worker dead

## Symptom

`/status` shows `worker-heartbeat` as **Unhealthy** (or Degraded), and sources sit in
**Queued** without becoming Processed. Nothing else looks wrong — the API answers, pages
render, uploads succeed. That is the point: a dead worker breaks nothing you can see.

There is no alert for this yet. `/status` is how you find out.

## First: is it actually broken?

**An idle worker is normal.** `ca-nornis-worker` runs at `minReplicas 0` and scales up on
queue depth, so zero replicas with an empty queue is correct and costs nothing. The check
knows this: with no sources awaiting extraction it reports Healthy no matter how long the
worker has been gone.

So `worker-heartbeat` Unhealthy means specifically: **there is work outstanding and
nothing has picked it up for more than fifteen minutes.**

```bash
curl -s https://api.nornis.app/status
```

Degraded rather than Unhealthy usually means the worker is mid-cold-start against work
that just arrived. Wait two minutes and look again before doing anything.

## Diagnose

```bash
# Is anything running?
az containerapp replica list -n ca-nornis-worker -g rg-nornis -o table

# Is there work to pick up? Both queues, active and dead-lettered.
az servicebus queue list -g rg-nornis --namespace-name sb-nornis-dev \
  --query "[].{queue:name, active:countDetails.activeMessageCount, dlq:countDetails.deadLetterMessageCount}" -o table

# What did it say before it stopped?
az containerapp logs show -n ca-nornis-worker -g rg-nornis --tail 200
```

Three shapes, three causes:

| Replicas | Queue | Meaning |
| --- | --- | --- |
| 0 | has messages | The scaler is not waking it — check the `queue-depth` scale rule and its `sb-manage` secret |
| 1, restarting | has messages | Crash loop. The logs say why; a bad config value is the usual cause |
| 1, quiet | has messages | It is up but not receiving — Service Bus connection or lock problems |

## Remedy

Restart by forcing a new revision:

```bash
az containerapp update -n ca-nornis-worker -g rg-nornis
```

If it is a crash loop from a bad deploy, roll back to the previous revision:

```bash
az containerapp revision list -n ca-nornis-worker -g rg-nornis -o table
az containerapp revision activate -n ca-nornis-worker -g rg-nornis --revision <previous>
```

If messages are dead-lettering rather than the worker being down, this is the wrong
runbook — see [dead-letter-queue.md](dead-letter-queue.md).

## Verify

```bash
# Replica up
az containerapp replica list -n ca-nornis-worker -g rg-nornis -o table

# Queue draining — active count should fall
az servicebus queue show -g rg-nornis --namespace-name sb-nornis-dev -n source-extraction \
  --query countDetails -o json

# And the check agrees
curl -s https://api.nornis.app/status
```

Done when `worker-heartbeat` is Healthy **and** the queue's active count is falling. A
Healthy check with a stalled queue means the worker is alive but not consuming, which is
a different problem from the one you just fixed.

## Notes

`az containerapp update` kills mid-extraction work. That is safe because Service Bus
redelivers, but the interrupted source will be re-extracted from the start and pay its AI
cost again.
