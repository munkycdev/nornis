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

There is no peek/resubmit script yet — that is O2 in
`docs/plans/operational-hardening.md`. Until it exists, use Service Bus Explorer in the
portal:

> Service Bus → `sb-nornis-dev` → Queues → `source-extraction` → Service Bus Explorer →
> switch to **Dead-letter** → Peek to read, **Resubmit** to send back to the active queue.

Peek first, always. The dead-letter reason and description are on each message and tell
you which of the causes above you are looking at.

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
