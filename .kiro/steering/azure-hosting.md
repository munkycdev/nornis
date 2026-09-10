# Azure Hosting and Infrastructure

> **Amendment (July 2026):** MVP hosting runs on **Azure Container Apps**, not AKS —
> same containers and ACR as spec'd below, but no cluster to operate: built-in HTTPS
> ingress, sticky sessions for the Blazor Server circuit, and a KEDA Service Bus scale
> rule that runs the worker only when the extraction queue has messages. Provisioning
> is scripted in `scripts/provision-azure.ps1` (az CLI; Terraform deferred until a
> second environment exists). Deployment is `.github/workflows/deploy.yml`. The AKS
> plan below remains the scale-up path if Container Apps is ever outgrown.

> **Amendment (2026-09-07):** images live in **GitHub Container Registry**
> (`ghcr.io/munkycdev/nornis-{api,web,worker}`), not ACR. `acrnornis` was deleted in the
> September cost pass: the repo is public, so its packages are public and GHCR hosts them
> for nothing, where ACR Basic was $5/month — a sixth of the whole bill — and within a few
> deploys of its 10 GB ceiling. The container apps pull anonymously, so the `AcrPull` role
> and the registry credential are gone too; `id-nornis-apps` remains, holding no roles,
> as the identity the operational-hardening plan intends to give Blob and Service Bus
> access to. Read "Azure Container Registry" below as GHCR. Everything else in the July
> amendment stands.

> **Amendment (2026-09-08):** the API and worker reach **SQL, Blob Storage and Service Bus
> as their own system-assigned managed identities** (O3 in the operational-hardening plan).
> Configuration carries endpoints — a password-less SQL connection string with
> `Authentication=Active Directory Default`, `BlobStorage:ServiceUri`, and
> `AzureServiceBus:FullyQualifiedNamespace` / `ServiceBus:FullyQualifiedNamespace` — and the
> rule that turns them into clients lives once, in `Nornis.Infrastructure/Configuration/AzureClients.cs`:
> a connection string wins if one is set (the local emulator stack still needs them),
> otherwise the endpoint is reached as the process's identity, otherwise the resource is
> not configured. Roles: each app has Storage Blob Data Contributor on the `nornis-library`
> container and Service Bus Data Sender (the worker also Receiver) on the namespace, and a
> contained SQL user with read/write (`scripts/sql-identity-users.cs`; the server's Entra
> admin is David). KEDA reads queue depth as the shared user-assigned `id-nornis-apps`,
> which holds Service Bus Data Owner for exactly that, so the API never carries Manage.
> SAS URLs for browser uploads are signed with a user delegation key. The only remaining
> app secrets are the two Azure OpenAI keys and the Auth0 client secret. Read "Secrets"
> below in that light. *(2026-09-09: the SQL server is Entra-only — no SQL logins work at
> all — and shared-key access on the storage account is off; every path into the three
> stores is an identity.)*

> **Amendment (2026-09-09):** there is no Key Vault. Nothing provisions one and nothing reads
> from one; the "Secrets" section below describes a mechanism that was never built. The three
> app secrets that remain — the two Azure OpenAI keys and the Auth0 client secret — are
> **Container Apps secrets**, set by `scripts/provision-azure.ps1` from the .NET user-secrets
> stores on the operator's machine and referenced from each app's environment as `secretref:`
> values (the Application Insights connection string travels the same way). The rest of that
> section's list no longer exists at all: since 2026-09-08 there are no database, Service Bus or
> Blob credentials to store.

## Hosting Target

Nornis will be hosted on Azure Kubernetes Service.

Primary Azure resources:

- Azure Kubernetes Service
- Azure Container Registry
- Azure SQL Database
- Azure Blob Storage
- Azure Service Bus
- Azure Key Vault
- Azure OpenAI
- Managed identities
- DataDog integration

## Infrastructure as Code

Use Terraform for Azure infrastructure.

Terraform owns:

- Resource groups
- AKS cluster
- ACR
- Azure SQL server/database
- Blob storage accounts/containers
- Service Bus namespace/queues
- Key Vault
- Managed identities
- Role assignments
- Network resources as needed

Terraform should not be used to manage every application deployment detail inside Kubernetes unless intentionally chosen.

## Kubernetes App Deployment

Prefer Helm charts or clear Kubernetes manifests for application deployment.

Application deployment owns:

- Deployments
- Services
- Ingress
- ConfigMaps
- SecretProviderClass mappings
- HorizontalPodAutoscaler if used

## Recommended Services

```text
nornis-web
nornis-api
nornis-worker
```

MVP may combine web and API if that simplifies delivery, but code should maintain clear boundaries.

## Namespaces

Use separate Kubernetes namespaces per environment:

```text
nornis-dev
nornis-prod
```

## Container Registry

Use Azure Container Registry.

Images:

```text
nornis-web
nornis-api
nornis-worker
```

Tag images with:

- Git SHA
- Semantic version when available
- Environment deployment labels where useful

## Secrets

Use Azure Key Vault.

Access from AKS should use managed identity and Key Vault CSI driver or equivalent secure mechanism.

Secrets include:

- Auth0 domain/audience/client configuration where secret
- Database connection strings
- Azure OpenAI keys or managed identity configuration
- DataDog API key
- Service Bus connection if not using identity
- Blob storage credentials if not using identity

Do not commit secrets to source control.

## Service Bus

Use Azure Service Bus for async extraction.

Suggested queues:

```text
source-extraction
source-extraction-deadletter via native DLQ
```

Messages should contain IDs and metadata, not large source bodies.

## Azure SQL

Use Azure SQL as the source of truth.

Guidelines:

- Use EF Core migrations.
- Apply migrations as part of controlled deployment.
- Avoid destructive migrations without explicit approval.
- Ensure backups/restore are configured appropriately for production.

## Blob Storage

Use Blob Storage for uploads and source attachments.

Suggested containers:

```text
world-sources
```

Use world/user metadata in blob pathing, but do not rely on path structure alone for authorization. Authorization must be enforced by the API.

## Ingress and TLS

Use a Kubernetes ingress controller appropriate for AKS.

Decide before implementation:

- Ingress controller choice
- TLS certificate management
- DNS configuration

Public ingress shape:

```text
https://nornis.app          Blazor web app
https://api.nornis.app      API
```

Web and API are separate hosts with separate ingress rules.

## DataDog

Use DataDog for observability.

Services should emit consistent tags:

```text
service:nornis-api
service:nornis-web
service:nornis-worker
env:dev|prod
version:<git-sha>
```

Track AI-specific metrics:

- AI operation duration
- Input tokens
- Output tokens
- Estimated cost
- Extraction failure count
- Proposal count

## Cost Awareness

Azure resources should be provisioned conservatively for MVP.

AKS can become expensive and operationally noisy. Avoid over-scaling early.

Cost visibility is a product feature and an operational requirement.
