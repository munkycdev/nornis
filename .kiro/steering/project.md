# Nornis - AI-First World Memory

> **Amendment (2026-09-09):** four lines of the Tech Stack below name tools the system does
> not use, and the Build & Run section was never filled in. Hosting is **Azure Container Apps**
> (`ca-nornis-{api,web,worker}`), not AKS; images live in **GitHub Container Registry**
> (`ghcr.io/munkycdev/nornis-{api,web,worker}`), not ACR; observability is **Application
> Insights via OpenTelemetry**, not DataDog; and infrastructure is provisioned by
> `scripts/provision-azure.ps1` with the az CLI, Terraform deferred until a second environment
> exists. Since 2026-09-08 the API and worker reach SQL, Blob Storage and Service Bus as their
> own managed identities. Each of those is recorded, with its reasoning, in `azure-hosting.md`,
> `cicd.md` and `observability-and-costs.md`; this note exists so the summary here stops
> disagreeing with them.
>
> Build & Run, as the tree has it (README.md is canonical):
>
> ```powershell
> dotnet build Nornis.sln
> dotnet test --solution Nornis.sln                # Microsoft.Testing.Platform runner, per global.json
> dotnet format --verify-no-changes --no-restore   # the CI format check
> ```
>
> Warnings are errors (`Directory.Build.props`).
>
> Two Conventions bullets have also drifted: `.kiro/specs/` is empty — design decisions live in
> `docs/features/` (per-feature docs, numbered in build order) and `docs/plans/` — and
> kebab-case holds for scripts, docs and workflows, while C# files and folders follow the .NET
> convention the whole tree uses (`Controllers/ArtifactsController.cs`).

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
