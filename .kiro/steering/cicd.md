# CI/CD

> **Amendment (July 2026):** Three departures from the text below, all settled.
> Infrastructure is provisioned by **`scripts/provision-azure.ps1`** (az CLI), not
> Terraform — deferred until a second environment exists (see `azure-hosting.md`);
> the Terraform Requirements section is dormant until then. Deployment targets
> **Azure Container Apps**, not AKS: `deploy.yml` builds and pushes images to ACR,
> then runs `az containerapp update` on `ca-nornis-{api,web,worker}` — no manifests,
> no Helm, and "Kubernetes probes" reads as Container Apps probes. The workflow set
> is `ci.yml` (PRs: build, test, format) plus `deploy.yml` (main: test-gated
> rollout), not the five-file layout suggested below; there is one environment
> (prod), deployed automatically from main.

## Tooling

Use GitHub Actions for CI/CD.

Use Terraform for Azure infrastructure.

Use containerized deployments to AKS.

## Pipeline Goals

Every pull request should run:

- Restore
- Build
- Unit tests
- Formatting check
- Static analysis where practical

Every main branch merge should:

- Build containers
- Push images to Azure Container Registry
- Run Terraform plan/apply for appropriate environment when configured
- Deploy application manifests or Helm chart to AKS

## Suggested Workflows

```text
ci.yml
infra-plan.yml
infra-apply.yml
deploy-dev.yml
deploy-prod.yml
```

## Build Requirements

- Treat warnings seriously.
- Keep builds deterministic.
- Do not allow tests to depend on local machine state.
- Do not print secrets.
- Publish test results where useful.

## Container Requirements

- Build minimal runtime images.
- Use non-root containers where practical.
- Include version metadata labels.
- Tag images with Git SHA.

Suggested image tags:

```text
<git-sha>
main-latest
v<semver>
```

## Terraform Requirements

Terraform should run with remote state.

Plan should be visible before apply.

Production apply should require manual approval.

Do not store Terraform secrets in repository.

## Deployment Requirements

Deployments should be repeatable.

Migrations should be explicit and safe.

The app should expose a health endpoint that can be used by Kubernetes probes.

Allowed anonymous endpoint:

```text
GET /health
```

## Environments

At minimum:

```text
dev
prod
```

Development deployments may be automatic from main.

Production deployments should require approval.

## Quality Gates

Initial quality gates:

- Build passes.
- Unit tests pass.
- Formatting passes.
- No known high-severity dependency or code scanning issues where configured.

Do not optimize for fake coverage. Tests should protect logic, not flatter a dashboard like a desperate courtier.
