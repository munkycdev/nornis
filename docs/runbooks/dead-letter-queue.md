# Dead-letter queue non-empty

## Symptom

`nornis-sb-deadletter` fires: *"A worker message dead-lettered — an extraction or
indexing job failed permanently."*

A message dead-letters after Service Bus has redelivered it its maximum number of times
and the worker failed every attempt. The source that message belonged to is stuck: it will
never be extracted, and nothing in the UI says so.

## Diagnose

Which queue, and how many:

```bash
az servicebus queue list -g rg-nornis --namespace-name sb-nornis-dev \
  --query "[].{queue:name, active:countDetails.activeMessageCount, dlq:countDetails.deadLetterMessageCount}" -o table
```

`source-extraction` and `library-indexing` fail for different reasons — extraction is a
paid AI call, indexing is PDF parsing and embeddings.

Then find out why. The worker logs its failures before abandoning:

```bash
az containerapp logs show -n ca-nornis-worker -g rg-nornis --tail 500 \
  | grep -iE "error|exception|abandon|fail"
```

Common causes, in rough order of likelihood:

| Cause | Tell |
| --- | --- |
| Azure OpenAI content filter | The source text is grisly; the call is rejected every time, deterministically |
| Budget cap reached | Guard refuses before calling — see [budget-cap-hit.md](budget-cap-hit.md) |
| Malformed or huge source | Parse failures, token-limit errors |
| Transient outage that outlasted redelivery | Errors stop on their own; the message just ran out of attempts |

The distinction that matters: **deterministic or transient?** Resubmitting a
content-filter rejection just dead-letters it again.

## Remedy

`scripts/dlq.ps1` does all three moves. It reads the `sb-manage` secret through your own
`az login`, so it needs the rights you would need in the portal and grants the running
system nothing.

```powershell
./scripts/dlq.ps1                                        # peek — non-destructive
./scripts/dlq.ps1 -Queue library-indexing                # the other queue
./scripts/dlq.ps1 -Action Resubmit -Count 5
./scripts/dlq.ps1 -Action Purge -Count 100
```

**Peek first, always.** It prints each message's dead-letter reason and description, which
is what tells you which of the causes above you are looking at. Peek holds locks while it
walks and releases them at the end, so the queue is exactly as you found it.

- **Transient** (the outage is over): `-Action Resubmit`. Messages go back to the live
  queue and are picked up on the next scale-up.
- **Deterministic** (content filter, malformed source): resubmitting changes nothing. Fix
  the source in the UI and re-run extraction from there, which enqueues a fresh message.
  Then `-Action Purge` the dead-lettered one so the alert clears.

- **Transient** (the outage is over): resubmit. It will be picked up on the next scale-up.
- **Deterministic** (content filter, malformed source): resubmitting changes nothing.
  Soften or fix the source in the UI and re-run extraction from there, which enqueues a
  fresh message. Then purge the dead-lettered one so the alert clears.

## Verify

```bash
az servicebus queue show -g rg-nornis --namespace-name sb-nornis-dev -n source-extraction \
  --query countDetails -o json
```

`deadLetterMessageCount` back to 0, and the source reaching Processed in the UI. The alert
resolves once the count stays down.

## Notes

An empty DLQ is not the same as work completing. A resubmitted message that fails again
lands straight back here — if the count returns within minutes, stop resubmitting and
treat the cause as deterministic.

There is deliberately **no dead-letter row on `/status`**. Reading queue depth needs Manage
rights on the namespace, and the API holds a Send-only key by design; the only way to put
that number on the page would be to give the most exposed component in the system queue
administration. The alert already covers detection, and the count is an operator's
question rather than a public one.
