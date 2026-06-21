# Nornis - AI-First Knowledge Management for D&D

## Project Overview

Nornis is an AI-first knowledge management tool designed for Dungeons & Dragons. It helps Dungeon Masters and players organize, retrieve, and generate campaign knowledge using AI capabilities.

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

- Use kebab-case for file and folder names
- Use clear, descriptive naming throughout the codebase
- Document design decisions in .kiro/specs/
- Every project has a corresponding test project
- Every class has a corresponding test class
- Repository pattern over direct DbContext usage

## Build & Run

<!-- Update with actual commands once the project is set up -->
- Build: TBD
- Test: TBD
- Lint: TBD
