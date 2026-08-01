# AI call failures

## Symptom

`nornis-ai-call-failures` fires (severity 1). `/status` may show `azure-openai` as
**Degraded**, though it will not if the failures are only in the worker — that check reads
API-side calls only, because the monitor behind it is per-process.

The alert exists because of a specific incident: on 2026-07-27 an unsupported
`max_tokens` parameter took every AI feature down, and the same rejection had silently
failed world-name generation for two days before anyone noticed. It fires on any AI
failure, including ones a caller swallows into a fallback.

## Diagnose

```bash
az monitor app-insights query --app appi-nornis -g rg-nornis --offset 2h --analytics-query "
union exceptions, traces
| extend Text = strcat(tostring(outerMessage), ' ', tostring(innermostMessage), ' ', tostring(message))
| where Text has_any ('AI call failed', 'AI extraction call failed', 'unsupported_parameter', 'invalid_request_error', 'World name generation failed')
| project timestamp, cloud_RoleName, Text
| order by timestamp desc
| take 20" -o table
```

`--offset` is required; the CLI defaults to a window that will not include what you want.
`cloud_RoleName` tells you `nornis-api` or `nornis-worker`, which narrows it immediately.

| In the text | Cause | Scope |
| --- | --- | --- |
| `unsupported_parameter`, `invalid_request_error` | A request shape the deployment rejects — usually after a model or SDK change | Everything, immediately |
| `content_filter` | One grisly source, rejected deterministically | That source only |
| `429`, rate limit | Throttling | Transient, self-healing |
| Budget messages | Not a failure — see [budget-cap-hit.md](budget-cap-hit.md) | That world |

The distinction that decides your next move: **is every call failing, or one kind of
call?** A parameter rejection kills everything and needs a fix now. A content filter
affects one source and needs no action beyond softening that text.

## Remedy

**Every call failing after a deploy or model change.** This is the 2026-07-27 shape. Roll
the API back and confirm recovery before diagnosing further:

```bash
az containerapp revision list -n ca-nornis-api -g rg-nornis -o table
az containerapp revision activate -n ca-nornis-api -g rg-nornis --revision <previous>
```

**Every call failing with no deploy.** Suspect Azure OpenAI itself. Check the resource in
the portal and Azure status. Nothing to fix here — extraction messages will redeliver, and
the worker retries. Watch the dead-letter count so redelivery exhaustion does not turn a
transient outage into stuck sources: [dead-letter-queue.md](dead-letter-queue.md).

**One source failing repeatedly.** Content filter. Soften the text in the UI and re-run
extraction. Do not resubmit the dead-lettered message — it will fail identically.

## Verify

```bash
curl -s https://api.nornis.app/status     # azure-openai Healthy
```

Then exercise a real call — ask the Loremaster something in the app — and re-run the KQL
above over the last 15 minutes to confirm nothing new has landed. The `/status` check
reports Healthy when idle, so an absence of traffic looks identical to recovery. Make
traffic before believing it.
