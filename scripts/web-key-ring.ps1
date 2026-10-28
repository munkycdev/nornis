<#
.SYNOPSIS
Gives the live Web app the key-ring blob it needs before its sign-in can start.

provision-azure.ps1 sets this up for a newly created ca-nornis-web, but the live app predates
2026-10-08, when the key ring moved off the container filesystem. Since then the Web app
refuses to start without DataProtection:KeyBlobUri, and the deploy only swaps images: every
Web revision after that change failed to start and the previous one kept serving, so no Web
change reached production until this ran.

Does what provision-azure.ps1 does for the Web key ring, against the existing app: creates the
container, turns on the app's system-assigned identity, grants that identity (and only that
identity) Storage Blob Data Contributor on the container, and sets the setting. Idempotent.

Run it before the next deploy. The role can take a few minutes to take effect, and the revision
this creates still runs the old image, which ignores the setting.

  ./scripts/web-key-ring.ps1
#>
#Requires -Version 7.3
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$ResourceGroup = "rg-nornis",
    [string]$WebApp = "ca-nornis-web",
    [string]$StorageAccount = "stchronicis",
    [string]$StorageRg = "rg-nornis",
    [string]$KeyContainer = "nornis-web-keys"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

Write-Host "== Key container"
# Through ARM like provision-azure.ps1: a data-plane create needs a blob data role the person
# running this usually does not hold.
$exists = az storage container-rm exists --storage-account $StorageAccount -g $StorageRg -n $KeyContainer --query exists -o tsv
if ($exists -ne "true") {
    az storage container-rm create --storage-account $StorageAccount -g $StorageRg -n $KeyContainer --public-access off -o none
}
$storageAccountScope = az storage account show -g $StorageRg -n $StorageAccount --query id -o tsv
$keyScope = "$storageAccountScope/blobServices/default/containers/$KeyContainer"

Write-Host "== Web identity"
$principal = az containerapp identity assign -g $ResourceGroup -n $WebApp --system-assigned --query principalId -o tsv
if (-not $principal) { throw "$WebApp has no system-assigned identity after assigning one." }

Write-Host "== Role on the key container"
$role = "Storage Blob Data Contributor"
# Counted here rather than with a JMESPath length(): az is a .cmd on Windows, and cmd.exe
# cannot take parentheses in an argument.
$granted = @(az role assignment list --assignee $principal --role $role --scope $keyScope --query "[].id" -o tsv)
if ($granted.Count -eq 0) {
    az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal `
        --role $role --scope $keyScope -o none
}

Write-Host "== Setting"
$blobServiceUri = az storage account show -g $StorageRg -n $StorageAccount --query primaryEndpoints.blob -o tsv
az containerapp update -g $ResourceGroup -n $WebApp `
    --set-env-vars "DataProtection__KeyBlobUri=$($blobServiceUri)$KeyContainer/keys.xml" -o none

Write-Host "Done. Wait a few minutes for the role to take effect before deploying a new Web image."
