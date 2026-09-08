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

> **Amendment (2026-09-07):** a cost pass over the subscription. The bill had settled at
> about $31/month and two-thirds of it was fixed cost that did no work:
>
> - **Both availability tests now run from one location** (`us-ca-sjc-azr`), not two.
>   Standard web test executions were the largest single line on the bill at $6.45/month;
>   this halves it. The trade is that one failed ping trips `nornis-availability` with no
>   second location to corroborate — acceptable for a system with one operator.
> - **Log ingestion was a flat 180 MB/day regardless of traffic**, about 5.5 GB/month
>   against a 5 GB free grant, and nearly all of it was the readiness probe: /health every
>   ten seconds, each running three EF queries for the pending-migrations check, each query
>   logged twice at Information (console line + App Insights trace, both outside the request
>   sampler) and once more as a SQL dependency span. The HTTP client connection metrics
>   added ~2 GB/month on top. Four changes in code: EF Core logs at Warning in production
>   (API `appsettings.json`); the `http.client.*` instruments are dropped by a metrics view
>   in all three hosts; /health is filtered out of request tracing in API and Web; and
>   `PendingMigrationsHealthCheck` remembers an up-to-date schema for the process via
>   `MigrationStateMemo`, so the database is asked until it says yes and never again.
>   Expected volume afterwards is under 1 GB/month. The 0.5 GB daily cap and the ingestion
>   spike alert stay as the backstop.
> - **Images moved from ACR to GitHub Container Registry** (see `azure-hosting.md`).
> - **The two gpt-5.4 deployments moved from DataZoneStandard to GlobalStandard.** Nothing
>   in Nornis needs the data-zone residency guarantee, and Global is the cheaper meter.
>
> Not done, and why: web and api stay at one warm replica each (~$10/month idle). Scaling
> to zero only pays if every ping test also goes — each ping wakes a replica for five
> minutes at eight times the idle rate — and the cold start would land on the public demo
> world and shared Ask links. SQL Basic is already the floor for a database that is probed
> constantly, so serverless auto-pause would never engage.

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
