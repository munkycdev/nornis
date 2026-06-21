# Observability and Cost Tracking

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

Avoid high-cardinality tags such as raw source IDs or campaign IDs in metrics unless explicitly needed. For logs, campaign IDs may be included when safe and useful.

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
- Campaign ID where safe
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
- CampaignId: Guid?
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
- Usage by campaign
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
- Per-campaign daily usage threshold where practical.
- Logging and alerting for unusually high token usage.
- Alerts for repeated extraction failures.

Do not let the Loremaster quietly burn money in the basement like an unsupervised wizard with a corporate card.
