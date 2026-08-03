# Observability and Cost Tracking

> **Amendment (July 2026):** Observability runs on **Azure Monitor / Application
> Insights** (`appi-nornis`) via OpenTelemetry, not DataDog — read every DataDog
> reference below accordingly. The "service tags" exist as OTel service names
> (`nornis-api`, `nornis-web`, `nornis-worker`) on each host's resource. API and Web
> telemetry is sampled down (its volume scales with open browser tabs); the worker's
> is deliberately unsampled — low volume, and the most diagnostically valuable traces
> in the system. Alert rules and an availability test are live.
> Everything else below — the metric list, logging rules, and cost tracking as a
> product feature — stands as written.

> **Amendment (2026-08-02):** the sentence above said the availability test runs
> "against `/health`". It does not, and never did. `ping-nornis-app` requests
> `https://nornis.app/welcome` — a static marketing page on the **Web** app — and
> validates only that it returns 200. `nornis-availability` fires on that test's
> success rate, so for most of the project's life **no alert watched the API at all**: a
> missed migration, a crashed API revision, or a 503 from `/health` left the ping green,
> because `/welcome` renders without the API. Verified against the live resource, not
> inferred.
> **Closed the same day.** `ping-nornis-api-health` now pings `https://api.nornis.app/health`
> from the same two locations every 15 minutes, expecting 200 — and since `/health` answers
> 503 when a migration is pending, a missed migration finally pages someone.
>
> Adding it required changing the alert, not just adding a test. `nornis-availability` had no
> dimensions, so it averaged availability across *every* web test. A second test would have
> **reduced** sensitivity: an API at 82% alone crosses the 90% threshold, but averaged with a
> healthy Web app reads as 91% and stays quiet. The alert now splits on
> `availabilityResult/name`, so each test is judged alone and the notification names which one
> is down.

## Observability Tool

Use DataDog for logs, metrics, traces, and dashboards.

## Service Tags

Use consistent tags across all services:

```text
service:nornis-web
service:nornis-api
service:nornis-worker
env:dev|prod
version:<git-sha>
```

For AI operations, include safe tags where possible:

```text
operation_type
ai_model
source_type
```

Avoid high-cardinality tags such as raw source IDs or world IDs in metrics unless explicitly needed. For logs, world IDs may be included when safe and useful.

## Required Metrics

### API

- Request count
- Request duration
- Error count
- Auth failures
- Authorization failures

### Worker

- Extraction jobs queued
- Extraction jobs started
- Extraction jobs completed
- Extraction jobs failed
- Extraction duration
- Dead-letter count

### AI

- AI operation count
- AI operation duration
- Input tokens
- Output tokens
- Total tokens
- Estimated cost
- Model used
- Structured output parse failures

### Review

- Review batches created
- Proposals created
- Proposals accepted
- Proposals rejected
- Proposals edited

## Logging

Use structured logging.

Logs should include:

- Correlation ID
- User ID where safe
- World ID where safe
- Operation type
- Source ID where relevant
- Review batch ID where relevant

Do not log:

- Auth tokens
- Secrets
- Raw Authorization headers
- Full private prompts by default
- Sensitive GM-only source content unless deliberately configured and redacted

## Cost Tracking as Product Feature

Nornis must track AI token and dollar usage in the database.

## AiUsageRecord

```csharp
AiUsageRecord
- Id: Guid
- WorldId: Guid?
- UserId: Guid?
- OperationType: AiOperationType
- Model: string
- InputTokens: int
- OutputTokens: int
- TotalTokens: int
- EstimatedCostUsd: decimal
- SourceId: Guid?
- ReviewBatchId: Guid?
- DurationMs: int
- Succeeded: bool
- ErrorCode: string?
- CreatedAt: DateTimeOffset
```

```csharp
AiOperationType
- SourceExtraction
- ArtifactSummary
- AskLoremaster
- SourceExtractionRepair
```

## Cost Detail Page

Add a cost detail page to the application.

Navigation:

```text
Costs
```

The page should show:

- Today usage
- This week usage
- This month usage
- All-time usage
- Usage by world
- Usage by user
- Usage by operation type
- Usage by model
- Input/output token breakdown
- Estimated dollar total

MVP can use estimated costs based on configured per-model rates.

## Cost Configuration

Store model pricing in configuration.

Pricing changes over time, so avoid hardcoding in business logic.

Suggested configuration:

```json
{
  "AiPricing": {
    "Models": {
      "model-name": {
        "InputPerMillionTokensUsd": 0.00,
        "OutputPerMillionTokensUsd": 0.00
      }
    }
  }
}
```

## Operational Guardrails

Add basic controls:

- Per-user rate limits where practical.
- Per-world daily usage threshold where practical.
- Logging and alerting for unusually high token usage.
- Alerts for repeated extraction failures.

Do not let the Loremaster quietly burn money in the basement like an unsupervised wizard with a corporate card.
