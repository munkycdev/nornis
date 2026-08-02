# AI paused

## Symptom

`/status` shows `azure-openai` as **Degraded** with the text *"AI is paused by an operator"*
and whatever reason was typed when the switch was flipped. Users get a 503 with
`ai_paused` from Ask, continuity assessment and fix drafting. Sources stay **Queued**;
library documents stay **Indexing**.

If the text says anything else — *"the last N AI call(s) all failed"* — this is not your
runbook. Go to [ai-call-failures.md](ai-call-failures.md).

## Is this deliberate?

```bash
./scripts/ai-pause.ps1
```

`running` means nobody paused it and the Degraded row is something else. `PAUSED: <reason>`
with a timestamp means somebody did, and the reason is the first thing to read.

## Why you would pause

Per-world budgets cap spend over a day. They cannot stop it *now*, and they cannot stop it
at all when the problem is not spend:

- A provider incident where every call fails slowly, so retries pile up and the ledger fills
  with paid failures.
- A runaway loop — a replay, a bulk import — spending faster than anyone expected.
- A prompt or model change behaving badly in production, where stopping is cheaper than
  diagnosing live.

## Pause

```bash
./scripts/ai-pause.ps1 -Action Pause -Reason "Azure OpenAI incident, tracking DPS-1234"
```

The reason is required, and it is not paperwork: it is shown to users when a request is
refused, and it is what makes the status page read as a decision rather than a fault.

**What happens, and how fast.** Effective within about ninety seconds — hosts cache the flag
for a minute and the worker polls every twenty seconds.

- **Interactive paths** (Ask, assess, draft fix) refuse immediately once their cache turns
  over, with the reason in the message.
- **Queue workers stop consuming.** They do not receive-and-abandon, because a received
  message has spent a delivery count and five of those dead-letter work that was never
  broken. A message nobody receives costs nothing and waits indefinitely — so a pause
  strands no work, however long it lasts.
- **Nothing is lost.** Sources stay Queued, documents stay Indexing, and both resume when
  you resume.

## Resume

```bash
./scripts/ai-pause.ps1 -Action Resume
```

Workers restart consuming within about ninety seconds and drain the backlog that
accumulated. Expect a burst: everything queued during the pause arrives at once, and the
per-world daily budgets are what bound the spend from here.

## Verify

```bash
curl -s https://api.nornis.app/status
```

`azure-openai` returns to Healthy — or to the idle wording, if nothing has called since.

## If the switch does not seem to work

The gate **fails open**: when the flag cannot be read at all, the system treats AI as
running. That is deliberate, and it is the one case where a pause silently does nothing.

Failing closed would be worse in both directions. A database blip would become a total AI
outage — the exact incident this switch exists to end. And a pause you cannot read is a
pause you cannot lift, because lifting it needs the same database.

So if `ai-pause.ps1` reports PAUSED and the system keeps spending, check that the hosts can
reach SQL — `/status` `sql` row — before suspecting the flag.
