# Azure Hosting and Infrastructure

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
campaign-sources
```

Use campaign/user metadata in blob pathing, but do not rely on path structure alone for authorization. Authorization must be enforced by the API.

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
