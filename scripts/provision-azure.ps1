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
#>
param(
    [string]$ResourceGroup = "rg-nornis",
    [string]$Location = "westus",
    [string]$Acr = "acrnornis",
    [string]$Environment = "cae-nornis",
    [string]$LogAnalytics = "log-nornis",
    [string]$ServiceBusRg = "rg-chronicis-dev",
    [string]$ServiceBusNamespace = "sb-nornis-dev",
    [string]$Queue = "source-extraction",
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

Write-Host "== Service Bus scaler policy (KEDA needs Manage to read queue depth)"
az servicebus queue authorization-rule create --resource-group $ServiceBusRg `
    --namespace-name $ServiceBusNamespace --queue-name $Queue `
    --name keda-scaler --rights Manage Listen Send -o none

Write-Host "== Collecting secrets (values are never printed)"
$sqlConn        = Get-UserSecret "src/Nornis.Api"    "ConnectionStrings:DefaultConnection"
$sbSend         = Get-UserSecret "src/Nornis.Api"    "AzureServiceBus:ConnectionString"
$sbListen       = Get-UserSecret "src/Nornis.Worker" "ServiceBus:ConnectionString"
$loreKey        = Get-UserSecret "src/Nornis.Api"    "Loremaster:AiKey"
$loreEndpoint   = Get-UserSecret "src/Nornis.Api"    "Loremaster:AiEndpoint"
$extractKey     = Get-UserSecret "src/Nornis.Worker" "Extraction:AiApiKey"
$extractEndpoint= Get-UserSecret "src/Nornis.Worker" "Extraction:AiEndpoint"
$sbManage = az servicebus queue authorization-rule keys list --resource-group $ServiceBusRg `
    --namespace-name $ServiceBusNamespace --queue-name $Queue --name keda-scaler `
    --query primaryConnectionString -o tsv

# A user-assigned identity shared by the apps for AcrPull keeps registry creds out of config.
Write-Host "== Managed identity for image pulls"
az identity create --name id-nornis-apps --resource-group $ResourceGroup -o none
$identityId = az identity show -g $ResourceGroup -n id-nornis-apps --query id -o tsv
$identityPrincipal = az identity show -g $ResourceGroup -n id-nornis-apps --query principalId -o tsv
$acrId = az acr show -n $Acr --query id -o tsv
az role assignment create --assignee-object-id $identityPrincipal `
    --assignee-principal-type ServicePrincipal --role AcrPull --scope $acrId -o none

# NOTE: ASPNETCORE_ENVIRONMENT=Development keeps the dev-auth bypass active. This is a
# deliberate, temporary decision (auth is not built yet) — the app is publicly usable by
# anyone with the URL. Remove when Auth0 lands.
Write-Host "== API app"
az containerapp create --name ca-nornis-api --resource-group $ResourceGroup `
    --environment $Environment --registry-server $acrServer --registry-identity $identityId `
    --user-assigned $identityId `
    --image "$acrServer/nornis-api:$ImageTag" --target-port 8080 --ingress external `
    --min-replicas 1 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
    --secrets sql-conn="$sqlConn" sb-send="$sbSend" lore-key="$loreKey" `
    --env-vars ASPNETCORE_ENVIRONMENT=Development `
        ConnectionStrings__DefaultConnection=secretref:sql-conn `
        AzureServiceBus__ConnectionString=secretref:sb-send `
        Loremaster__AiKey=secretref:lore-key `
        Loremaster__AiEndpoint="$loreEndpoint" -o none

$apiFqdn = az containerapp show -g $ResourceGroup -n ca-nornis-api --query properties.configuration.ingress.fqdn -o tsv

Write-Host "== Web app (sticky sessions for the Blazor Server circuit)"
az containerapp create --name ca-nornis-web --resource-group $ResourceGroup `
    --environment $Environment --registry-server $acrServer --registry-identity $identityId `
    --user-assigned $identityId `
    --image "$acrServer/nornis-web:$ImageTag" --target-port 8080 --ingress external `
    --min-replicas 1 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
    --env-vars ASPNETCORE_ENVIRONMENT=Development `
        Api__BaseUrl="https://$apiFqdn" -o none
az containerapp ingress sticky-sessions set --affinity sticky `
    -g $ResourceGroup -n ca-nornis-web -o none

Write-Host "== Worker app (scale-to-zero on queue depth)"
az containerapp create --name ca-nornis-worker --resource-group $ResourceGroup `
    --environment $Environment --registry-server $acrServer --registry-identity $identityId `
    --user-assigned $identityId `
    --image "$acrServer/nornis-worker:$ImageTag" `
    --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
    --secrets sql-conn="$sqlConn" sb-listen="$sbListen" sb-manage="$sbManage" extract-key="$extractKey" `
    --env-vars ConnectionStrings__DefaultConnection=secretref:sql-conn `
        ServiceBus__ConnectionString=secretref:sb-listen `
        Extraction__AiApiKey=secretref:extract-key `
        Extraction__AiEndpoint="$extractEndpoint" `
    --scale-rule-name queue-depth --scale-rule-type azure-servicebus `
    --scale-rule-metadata "queueName=$Queue" "messageCount=1" `
    --scale-rule-auth "connection=sb-manage" -o none

Write-Host ""
Write-Host "Provisioned. Public hosts:"
Write-Host "  web: https://$(az containerapp show -g $ResourceGroup -n ca-nornis-web --query properties.configuration.ingress.fqdn -o tsv)"
Write-Host "  api: https://$apiFqdn"
