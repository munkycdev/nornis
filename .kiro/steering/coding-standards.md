# Coding Standards

> **Amendment (2026-08-02):** the solution layout below still lists `Nornis.Shared` and
> `Nornis.Shared.Tests`. Both were deleted on 2026-08-01 (`1f21e55`): the project was
> empty, referenced by all five siblings, and its test project asserted `true`. Web owns
> its own contract types (`Contracts.cs`) and Api owns its request/response records, which
> is the deliberate decision the deletion recorded rather than reversed — so nothing moved
> into a replacement. Read the tree as six projects and six test projects. The rule below
> it, "every project must have a corresponding test project", is unchanged and still holds.

## General Principles

- Prefer clarity over cleverness.
- Use boring, reliable patterns unless there is a strong reason not to.
- Keep domain logic out of UI components.
- Keep authorization checks server-side.
- Keep AI prompt/response handling behind explicit interfaces.
- Avoid large god services.

## C# Standards

- Use nullable reference types.
- Use `DateTimeOffset` for timestamps.
- Prefer async all the way for I/O.
- Use cancellation tokens for API and worker operations where practical.
- Use records for immutable DTOs where useful.
- Use explicit enums for domain states.
- Avoid magic strings for domain states.

## Project Structure

Solution structure:

```text
src/
  Nornis.Web/
  Nornis.Api/
  Nornis.Worker/
  Nornis.Application/
  Nornis.Domain/
  Nornis.Infrastructure/
  Nornis.Shared/
tests/
  Nornis.Web.Tests/
  Nornis.Api.Tests/
  Nornis.Worker.Tests/
  Nornis.Application.Tests/
  Nornis.Domain.Tests/
  Nornis.Infrastructure.Tests/
  Nornis.Shared.Tests/
```

Web and API are separate hosts with separate deployable projects.

Every project must have a corresponding test project. Every class should have a corresponding test class.

## Test Framework

Use NUnit for all test projects.

## Domain Layer

Domain layer should not depend on:

- EF Core
- Azure SDKs
- Auth0 SDKs
- MudBlazor
- DataDog
- Azure OpenAI

## Application Layer

Application layer orchestrates use cases:

- Create source
- Enqueue extraction
- Apply proposal
- Reject proposal
- Query artifacts
- Ask Loremaster

Authorization should be enforced in application services or policies used by application services.

## Infrastructure Layer

Infrastructure implements:

- Repository implementations over EF Core DbContext
- Blob storage
- Service Bus
- Azure OpenAI
- DataDog integration
- Auth0 JWT configuration

Repository interfaces are defined in the Domain or Application layer. Infrastructure provides the concrete implementations using EF Core.

### The missing-row contract (added 2026-08-02)

Decided per verb, because the three answers previously in the tree disagreed about the
same situation:

- **A mutation of a row that is not there throws.** Callers reach these having just loaded
  and authorized the row, so its absence is a concurrent delete or a bug, never an ordinary
  outcome. Returning quietly makes the request report success for work it did not do.
  `NornisDbContext.LoadForUpdateAsync<T>(id, ct)` is the one way to load for a scoped write;
  it names the entity and the id in the exception, which a bare `FirstAsync` does not.
- **A delete of a row that is not there does nothing.** Delete is idempotent by nature — the
  caller wanted it gone and it is gone.

### Set-based writes

`ExecuteDelete` / `ExecuteUpdate` need a relational provider, and the API integration tests
run on InMemory. Use `DeleteWhereAsync` / `SetWhereAsync` rather than hand-writing the
`IsRelational()` branch or calling the bulk API unguarded. The exception is code that is
relational-only for other reasons (vector columns, explicit transactions) — leave the raw
call there and say why, so the constraint stays visible.

Applied across every repository as of 2026-08-02. Two deliberate non-candidates, so nobody
"finishes the job" by converting them:

- `LibraryChunkRepository.ReplaceForDocumentAsync` — relational-only anyway (SqlVector shadow
  property, explicit transaction), and the raw call says so.
- `StorylineCampaignRepository.ReplaceForStorylineAsync` — a set reconciliation, not a
  delete-where. It removes the rows absent from the desired set and adds the rest in one
  SaveChanges; a bulk delete would split that into two units of work and could not express
  the predicate anyway.

## API Layer

- Authenticate by default.
- Use explicit authorization policies.
- Never trust client-provided user IDs.
- Validate world membership for world-scoped endpoints.
- Return appropriate status codes.
- Avoid leaking existence of unauthorized resources where practical.

## Blazor UI

- Keep components focused.
- Move complex state management into services.
- Do not put domain mutation logic directly in components.
- Use reusable components for proposal cards, artifact cards, status badges, visibility badges, and cost summary cards.

## AI Integration

- Wrap AI calls behind interfaces.
- Store prompts/templates in maintainable structures.
- Use structured output schemas.
- Validate AI output before storing proposals.
- Track usage and cost for every AI call.

## Error Handling

- Fail safely.
- Show useful user-facing error messages.
- Log enough context for diagnosis.
- Do not expose stack traces to users.
- Do not swallow exceptions silently.

## Formatting

Use `.editorconfig`.

Run formatting in CI.

## Testing

Write tests for logic that matters.

Do not add vanity tests that assert the framework works.

Do not mock so aggressively that the test proves nothing.
