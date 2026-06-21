# Architecture

## Preferred Stack

- Language: C#
- UI: Blazor Web App
- Backend: ASP.NET Core
- Database: Azure SQL
- File storage: Azure Blob Storage
- AI: Azure OpenAI
- Auth: Auth0 with Discord as the first identity provider
- Hosting: Azure Kubernetes Service
- Container registry: Azure Container Registry
- Queue: Azure Service Bus
- Observability: DataDog
- IaC: Terraform
- CI/CD: GitHub Actions

## Application Shape

Three separate deployable services:

```text
nornis-web    (Blazor Web App)
nornis-api    (ASP.NET Core API)
nornis-worker (Background job processor)
```

Web and API are separate hosts. They are independently deployable and run as distinct processes.

The worker should process asynchronous extraction jobs from Azure Service Bus.

## Public Ingress

```text
https://nornis.app        Blazor web app
https://api.nornis.app    API
```

## Data Access Pattern

Use the repository pattern over EF Core DbContext.

- Define repository interfaces in the Domain or Application layer.
- Implement repositories in the Infrastructure layer using EF Core.
- Application services depend on repository interfaces, not DbContext directly.
- This keeps domain and application layers decoupled from EF Core.

## Logical Layers

```text
Presentation
Application
Domain
Infrastructure
```

### Presentation

- Blazor pages and components
- View models
- UI state
- Client-side validation

### Application

- Use case orchestration
- Authorization checks
- Review proposal workflows
- Source processing commands
- AI extraction orchestration

### Domain

- Campaign
- CampaignMember
- Source
- Artifact
- ArtifactFact
- ArtifactRelationship
- ReviewBatch
- ReviewProposal
- AI usage ledger

### Infrastructure

- EF Core / Azure SQL
- Blob Storage
- Service Bus
- Azure OpenAI client
- Auth0 JWT validation
- DataDog logging/metrics/tracing

## Source Processing Flow

```text
User creates source
    ↓
API validates membership and stores source
    ↓
API enqueues extraction message
    ↓
Worker processes source asynchronously
    ↓
Worker calls Azure OpenAI with structured output schema
    ↓
Worker creates ReviewBatch and ReviewProposal records
    ↓
UI displays proposal review queue
```

## Review Acceptance Flow

```text
User accepts proposal
    ↓
API validates campaign role and visibility
    ↓
Application service applies mutation
    ↓
Artifact / Fact / Relationship is created or updated
    ↓
SourceReference is created
    ↓
ReviewProposal marked Accepted
```

The AI must not directly mutate accepted artifacts, facts, or relationships.

## Database

Use Azure SQL as source of truth.

Guidelines:

- Use EF Core migrations.
- Use optimistic concurrency tokens for mutable records.
- Store JSON only where the structure is proposal-specific or naturally flexible.
- Avoid making JSON the primary data model for artifacts, facts, or relationships.
- Store UTC timestamps using `DateTimeOffset`.

## Blob Storage

Use Azure Blob Storage for:

- Uploaded images
- Uploaded documents
- Handwritten note images
- Source attachments

Database records should store metadata and blob references, not raw file content.

## Async and Reliability

Use Azure Service Bus for extraction jobs.

Guidelines:

- Source processing should be idempotent.
- Messages should include IDs, not large payloads.
- Failed jobs should mark source or review batch failure state.
- Use dead-letter behavior for repeated failures.
- Log and meter all AI calls.

## AKS Guidance

AKS is the hosting target. Keep cluster usage disciplined.

Recommended Kubernetes resources:

```text
Deployment: nornis-web
Deployment: nornis-api
Deployment: nornis-worker
Service: nornis-web
Service: nornis-api
Ingress: nornis.app (web)
Ingress: api.nornis.app (API)
ConfigMap: non-secret configuration
SecretProviderClass: Key Vault secret mounting
```

Suggested namespaces:

```text
nornis-dev
nornis-prod
```

Use managed identity where possible.

Use Key Vault for secrets.

Do not commit secrets to source control, Terraform files, Helm values, or GitHub Actions logs.

## Terraform vs Helm

Terraform should own Azure infrastructure:

- Resource groups
- AKS
- ACR
- Azure SQL
- Blob Storage
- Service Bus
- Key Vault
- Managed identities
- Networking as needed

Helm or Kubernetes manifests should own application deployment into AKS:

- Deployments
- Services
- Ingress
- ConfigMaps
- SecretProviderClass bindings

GitHub Actions orchestrates both.

## Environments

At minimum:

```text
dev
prod
```

Each environment should have separate:

- Azure SQL database
- Blob containers or storage account isolation
- Service Bus namespace or queue isolation
- Kubernetes namespace
- Auth0 app configuration where appropriate

## Design Principle

Prefer clarity over cleverness. If the architecture starts requiring a conspiracy board to explain, simplify it before proceeding.
