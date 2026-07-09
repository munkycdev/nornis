# Nornis - AI-First World Memory

## Project Overview

Nornis is an AI-first world memory engine for tabletop roleplaying games. It helps Game Masters and players capture world sources, extract structured world knowledge, review proposed updates, and consult that knowledge through an AI Loremaster.

Nornis is not a wiki-first application. The product model is:

```text
Sources → Artifacts → Storylines / Canon / Ask
```

Brand direction:

```text
It's your epic. Every source leaves a mark.
```

## Tech Stack

- Language: C#
- UI: Blazor Web App (MudBlazor + custom design tokens)
- Backend: ASP.NET Core
- Database: Azure SQL (EF Core with repository pattern)
- File storage: Azure Blob Storage
- AI: Azure OpenAI
- Auth: Auth0 (Discord identity provider)
- Hosting: Azure Kubernetes Service
- Container registry: Azure Container Registry
- Queue: Azure Service Bus
- Observability: DataDog
- IaC: Terraform
- CI/CD: GitHub Actions
- Test framework: NUnit

## Conventions

- Use kebab-case for file and folder names.
- Use clear, descriptive naming throughout the codebase.
- Document design decisions in `.kiro/specs/`.
- Every project has a corresponding test project.
- Every class has a corresponding test class.
- Repository pattern over direct DbContext usage.
- Use `Storyline`, not `Thread`, for narrative arcs and unresolved world developments.
- Use `Source`, not `Evidence`, for raw input material.

## Build & Run

<!-- Update with actual commands once the project is set up -->
- Build: TBD
- Test: TBD
- Lint: TBD
