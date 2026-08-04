# CLAUDE.md

Guidance for Claude Code working in the Nornis repo.

Start with [README.md](README.md) for what Nornis is and how to build it.

## Steering

The docs in `.kiro/steering/` are the operating instructions for this repo — treat
them with the same authority Kiro gives them. Nothing in them is advisory.

**Always** (read before changing anything):

- `coding-standards.md` — how code is written here; the authority on style and idiom.
- `pre-implementation-checks.md` — five questions to answer before writing a feature, each
  derived from a defect this codebase shipped. Cheap to run and they catch the structural
  mistakes, which are the ones that survive review.
- `domain-model.md` — entities and the deliberate vocabulary (Storyline, Source,
  Artifact, Fact, Relationship, Canon, Reveal). Naming drift is a defect.

**By task** (read the matching doc before starting that kind of work):

| Working on…                                   | Read first                    |
| --------------------------------------------- | ----------------------------- |
| Deciding what to build, scoping a feature     | `product-vision.md`, `mvp-scope.md`, `project.md` |
| Solution structure, project references, layers | `architecture.md`            |
| Tests                                          | `testing-strategy.md`        |
| Auth, roles, visibility, anything user-scoped  | `security-and-permissions.md` |
| AI extraction, Loremaster, review pipeline     | `ai-extraction.md`           |
| UI / Blazor components                         | `ui-design-system.md`        |
| Infrastructure, hosting, provisioning          | `azure-hosting.md`           |
| GitHub Actions, pipelines                      | `cicd.md`                    |
| Telemetry, alerts, cost tracking               | `observability-and-costs.md` |

**When a steering doc and the tree disagree** (it happens — e.g. a doc may name a
tool the system no longer uses): the tree is the authority on what *is*, the doc on
what is *intended*. Don't silently follow either — surface the mismatch, and if the
reality is the settled decision, amend the doc the way `azure-hosting.md` does it: a
dated amendment note at the top, original text left in place.
