# Testing Strategy

## Test Framework

Use NUnit for all test projects.

## Testing Philosophy

Tests should protect behavior, not decorate coverage reports.

High coverage is useful as a regression signal, but useless tests are worse than no tests because they create false confidence and make future change painful.

## Test Structure

Every project must have a corresponding test project. Every class should have a corresponding test class.

```text
src/Nornis.Domain/Models/Campaign.cs
    → tests/Nornis.Domain.Tests/Models/CampaignTests.cs

src/Nornis.Application/Services/SourceService.cs
    → tests/Nornis.Application.Tests/Services/SourceServiceTests.cs
```

This ensures no class is accidentally left untested and makes it easy to find tests for any given class.

## Test Priorities

Prioritize tests for:

1. Authorization and visibility.
2. Review proposal application.
3. AI structured output parsing and validation.
4. Source processing state transitions.
5. Artifact/fact/relationship mutations.
6. Cost ledger creation.
7. Campaign membership rules.

## Unit Tests

Use unit tests for domain and application behavior.

Important unit test areas:

- Campaign role authorization.
- Visibility filtering.
- Proposal acceptance/rejection/editing.
- Source processing status transitions.
- AI usage cost calculation.
- Artifact deduplication logic.
- Fact and relationship creation rules.

## Integration Tests

Use integration tests for:

- API authentication and authorization.
- EF Core persistence behavior.
- Review proposal application end-to-end.
- Source creation and queued processing handoff.

## AI Tests

Do not rely on live AI calls in normal CI.

Use:

- Contract tests for structured output schemas.
- Golden sample source inputs with expected proposal shapes.
- Fake AI client for application tests.
- Optional manual or scheduled live AI evaluation outside core CI.

## Authorization Tests

Authorization deserves explicit test coverage.

Test that:

- Anonymous requests are rejected except `/health` and approved `/status`.
- Non-members cannot access campaign resources.
- Players cannot see GMOnly content.
- Observers cannot mutate campaign state.
- Private content is visible only to its creator.
- AI Ask does not retrieve unauthorized content.

## UI Tests

MVP UI tests can be limited.

Focus on:

- Review proposal workflow.
- Source creation form.
- Cost page rendering basic data.

Do not over-invest in brittle UI automation early.

## Coverage

Track coverage, but do not worship it.

Suggested policy:

- Maintain meaningful coverage on domain/application layers.
- Do not write empty tests that only exercise property getters.
- Do not mock everything so heavily that tests prove nothing.

## Test Data

Use realistic campaign examples:

- Captain Voss
- Black Harbor
- Silver Key
- Missing Caravan

This makes tests easier to read than abstract foo/bar/baz sludge.
