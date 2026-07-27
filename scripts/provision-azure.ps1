<#
.SYNOPSIS
Provisions the Nornis hosting stack on Azure Container Apps.

Creates (idempotently): resource group, Log Analytics, Container Apps environment,
Azure Container Registry, and the three container apps (api, web, worker). Reads
secrets from the Api/Worker .NET user-secrets stores — never echoes them.

Deviation from .kiro/steering/azure-hosting.md (AKS): MVP hosts on Container Apps —
same containers + ACR, no cluster to operate, scale-to-zero worker via a KEDA
Service Bus scaler. Revisit AKS if/when scale demands it.

Prereqs: az CLI logged in; images pushed to ACR (deploy workflow or az acr build).

NOT REPRODUCED BY THIS SCRIPT — apply by hand after running it, or the apps come up broken.
Reconciled against the live stack on 2026-07-27; everything below exists in production but has
no source here, because the values live nowhere in the repo or in user-secrets:

  ca-nornis-api   Auth0__Domain, Auth0__Audience, Auth0__ClaimsNamespace
  ca-nornis-web   Auth0__Domain, Auth0__ClientId, Auth0__ClientSecret, Auth0__Audience

Without them the API rejects every token and the Web app cannot complete a login. Read the
current values back before re-provisioning:

  az containerapp show -g rg-nornis -n ca-nornis-api `
      --query "properties.template.containers[0].env" -o table

Also note WebPush:PublicKey / WebPush:PrivateKey exist in the Api and Worker user-secrets
stores but are set on neither live app, so browser push notifications are inert in production.
#>
param(
    [string]$ResourceGroup = "rg-nornis",
    [string]$Location = "westus",
    [string]$Acr = "acrnornis",
    [string]$Environment = "cae-nornis",
    [string]$LogAnalytics = "log-nornis",
    [string]$ServiceBusRg = "rg-nornis",
    [string]$ServiceBusNamespace = "sb-nornis-dev",
    [string]$Queue = "source-extraction",
    [string]$LibraryQueue = "library-indexing",
    [string]$AppInsights = "appi-nornis",
    [string]$ImageTag = "bootstrap"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

function Get-UserSecret([string]$project, [string]$key) {
    $line = dotnet user-secrets list --project (Join-Path $repo $project) 2>$null |
        Where-Object { $_ -like "$key = *" } | Select-Object -First 1
    if (-not $line) { throw "User secret '$key' not found in $project" }
    return ($line -replace [regex]::Escape("$key = "), "")
}

Write-Host "== Resource group"
az group create --name $ResourceGroup --location $Location -o none

Write-Host "== Log Analytics"
az monitor log-analytics workspace create --resource-group $ResourceGroup `
    --workspace-name $LogAnalytics --location $Location -o none
$logId = az monitor log-analytics workspace show -g $ResourceGroup -n $LogAnalytics --query customerId -o tsv
$logKey = az monitor log-analytics workspace get-shared-keys -g $ResourceGroup -n $LogAnalytics --query primarySharedKey -o tsv

Write-Host "== Container Apps environment"
az containerapp env create --name $Environment --resource-group $ResourceGroup `
    --location $Location --logs-workspace-id $logId --logs-workspace-key $logKey -o none

Write-Host "== Container registry"
az acr create --name $Acr --resource-group $ResourceGroup --sku Basic --admin-enabled false -o none
$acrServer = az acr show -n $Acr --query loginServer -o tsv

Write-Host "== Service Bus queues"
# Both queues must exist before the worker starts: a missing queue throws
# MessagingEntityNotFound out of StartProcessingAsync. The worker sets
# BackgroundServiceExceptionBehavior.Ignore so that only kills the affected processor rather
# than the whole host, but the affected feature is still dead until the queue exists.
# Properties mirror the live namespace as of 2026-07-27.
foreach ($q in @($Queue, $LibraryQueue)) {
    az servicebus queue create --resource-group $ServiceBusRg `
        --namespace-name $ServiceBusNamespace --name $q `
        --max-delivery-count 5 --lock-duration PT1M --default-message-time-to-live P14D -o none
}

Write-Host "== Service Bus scaler policy (KEDA needs Manage to read queue depth)"
# One rule per queue — KEDA authenticates per scale rule, and the worker scales to zero, so
# the library queue needs its own or an uploaded PDF never wakes the worker.
foreach ($q in @($Queue, $LibraryQueue)) {
    az servicebus queue authorization-rule create --resource-group $ServiceBusRg `
        --namespace-name $ServiceBusNamespace --queue-name $q `
        --name keda-scaler --rights Manage Listen Send -o none
}

Write-Host "== Collecting secrets (values are never printed)"
$sqlConn        = Get-UserSecret "src/Nornis.Api"    "ConnectionStrings:DefaultConnection"
$sbSend         = Get-UserSecret "src/Nornis.Api"    "AzureServiceBus:ConnectionString"
$sbListen       = Get-UserSecret "src/Nornis.Worker" "ServiceBus:ConnectionString"
$loreKey        = Get-UserSecret "src/Nornis.Api"    "Loremaster:AiKey"
$loreEndpoint   = Get-UserSecret "src/Nornis.Api"    "Loremaster:AiEndpoint"
$extractKey     = Get-UserSecret "src/Nornis.Worker" "Extraction:AiApiKey"
$extractEndpoint= Get-UserSecret "src/Nornis.Worker" "Extraction:AiEndpoint"
# Library indexing reads uploaded PDFs from blob storage. Without this the worker still runs
# (blob registration is lazy) but every indexing message fails.
$blobConn       = Get-UserSecret "src/Nornis.Api"    "BlobStorage:ConnectionString"

# KEDA authenticates against a single connection string; the scaler rule on $Queue carries
# Manage over the whole namespace path it was issued for, and both scale rules reference the
# same secret, so one lookup is enough.
$sbManage = az servicebus queue authorization-rule keys list --resource-group $ServiceBusRg `
    --namespace-name $ServiceBusNamespace --queue-name $Queue --name keda-scaler `
    --query primaryConnectionString -o tsv

# Telemetry is opt-in by connection string — all three apps no-op without it. The component is
# not created here (it long predates this script); look it up and warn rather than fail, so a
# fresh subscription can still provision a working stack.
$appInsightsConn = az monitor app-insights component show -g $ResourceGroup -a $AppInsights `
    --query connectionString -o tsv 2>$null
if (-not $appInsightsConn) {
    Write-Warning "Application Insights component '$AppInsights' not found in $ResourceGroup - apps will start with telemetry disabled."
}

# A user-assigned identity shared by the apps for AcrPull keeps registry creds out of config.
Write-Host "== Managed identity for image pulls"
az identity create --name id-nornis-apps --resource-group $ResourceGroup -o none
$identityId = az identity show -g $ResourceGroup -n id-nornis-apps --query id -o tsv
$identityPrincipal = az identity show -g $ResourceGroup -n id-nornis-apps --query principalId -o tsv
$acrId = az acr show -n $Acr --query id -o tsv
az role assignment create --assignee-object-id $identityPrincipal `
    --assignee-principal-type ServicePrincipal --role AcrPull --scope $acrId -o none

# NOTE: ASPNETCORE_ENVIRONMENT=Development is still set on the live apps. Auth0 has since
# landed, and the dev-auth bypass is additionally gated on the placeholder Auth0 domain
# (see src/Nornis.Api/Program.cs), so it cannot actually engage in production — but the
# environment name still affects error detail and other host defaults. Flipping it to
# Production is a deliberate change to make on its own, not a side effect of provisioning.
Write-Host "== API app"
$apiSecrets = @(
    "sql-conn=$sqlConn"
    "sb-send=$sbSend"
    "lore-key=$loreKey"
    "blob-conn=$blobConn"
)
$apiEnv = @(
    "ASPNETCORE_ENVIRONMENT=Development"
    "ConnectionStrings__DefaultConnection=secretref:sql-conn"
    "AzureServiceBus__ConnectionString=secretref:sb-send"
    "Loremaster__AiKey=secretref:lore-key"
    "Loremaster__AiEndpoint=$loreEndpoint"
    "BlobStorage__ConnectionString=secretref:blob-conn"
    "AiBudget__DailyWorldBudgetUsd=2"
)
if ($appInsightsConn) {
    $apiSecrets += "appi-conn=$appInsightsConn"
    $apiEnv     += "APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appi-conn"
}

az containerapp create --name ca-nornis-api --resource-group $ResourceGroup `
    --environment $Environment --registry-server $acrServer --registry-identity $identityId `
    --user-assigned $identityId `
    --image "$acrServer/nornis-api:$ImageTag" --target-port 8080 --ingress external `
    --min-replicas 1 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
    --secrets @apiSecrets `
    --env-vars @apiEnv -o none

$apiFqdn = az containerapp show -g $ResourceGroup -n ca-nornis-api --query properties.configuration.ingress.fqdn -o tsv

Write-Host "== Web app (sticky sessions for the Blazor Server circuit)"
$webEnv = @(
    "ASPNETCORE_ENVIRONMENT=Development"
    "Api__BaseUrl=https://$apiFqdn"
)
az containerapp create --name ca-nornis-web --resource-group $ResourceGroup `
    --environment $Environment --registry-server $acrServer --registry-identity $identityId `
    --user-assigned $identityId `
    --image "$acrServer/nornis-web:$ImageTag" --target-port 8080 --ingress external `
    --min-replicas 1 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
    --env-vars @webEnv -o none

# The Web app has no other secrets, so telemetry is wired in a follow-up update rather than
# threading an optional --secrets through the create call.
if ($appInsightsConn) {
    az containerapp secret set --name ca-nornis-web --resource-group $ResourceGroup `
        --secrets "appi-conn=$appInsightsConn" -o none
    az containerapp update --name ca-nornis-web --resource-group $ResourceGroup `
        --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appi-conn" -o none
}
az containerapp ingress sticky-sessions set --affinity sticky `
    -g $ResourceGroup -n ca-nornis-web -o none

Write-Host "== Worker app (scale-to-zero on queue depth)"
# Secrets and env vars are built as arrays so the Application Insights entry can be omitted
# when the component is absent, rather than injecting an empty connection string.
$workerSecrets = @(
    "sql-conn=$sqlConn"
    "sb-listen=$sbListen"
    "sb-manage=$sbManage"
    "extract-key=$extractKey"
    "blob-conn=$blobConn"
)
$workerEnv = @(
    "ConnectionStrings__DefaultConnection=secretref:sql-conn"
    "ServiceBus__ConnectionString=secretref:sb-listen"
    "Extraction__AiApiKey=secretref:extract-key"
    "Extraction__AiEndpoint=$extractEndpoint"
    "BlobStorage__ConnectionString=secretref:blob-conn"
    # Deliberate overrides of the appsettings defaults, matching the live app. The 180s AI
    # timeout in particular is load-bearing: vision reads and large extractions exceed the
    # 60s default, and dropping back to it reintroduces spurious transient failures.
    "Extraction__AiTimeoutSeconds=180"
    "AiBudget__DailyWorldBudgetUsd=2"
)
if ($appInsightsConn) {
    $workerSecrets += "appi-conn=$appInsightsConn"
    $workerEnv     += "APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appi-conn"
}

az containerapp create --name ca-nornis-worker --resource-group $ResourceGroup `
    --environment $Environment --registry-server $acrServer --registry-identity $identityId `
    --user-assigned $identityId `
    --image "$acrServer/nornis-worker:$ImageTag" `
    --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
    --secrets @workerSecrets `
    --env-vars @workerEnv `
    --scale-rule-name queue-depth --scale-rule-type azure-servicebus `
    --scale-rule-metadata "queueName=$Queue" "messageCount=1" `
    --scale-rule-auth "connection=sb-manage" -o none

# `containerapp create` accepts a single scale rule, so the library queue's rule is added in a
# follow-up update. Without it the worker — at min-replicas 0 — never wakes for an uploaded
# PDF, and the document sits in Indexing until an unrelated extraction happens to start it.
Write-Host "== Worker scale rule for the library queue"
az containerapp update --name ca-nornis-worker --resource-group $ResourceGroup `
    --scale-rule-name library-queue-depth --scale-rule-type azure-servicebus `
    --scale-rule-metadata "queueName=$LibraryQueue" "messageCount=1" `
    --scale-rule-auth "connection=sb-manage" -o none

Write-Host ""
Write-Host "Provisioned. Public hosts:"
Write-Host "  web: https://$(az containerapp show -g $ResourceGroup -n ca-nornis-web --query properties.configuration.ingress.fqdn -o tsv)"
Write-Host "  api: https://$apiFqdn"
