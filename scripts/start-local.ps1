<#
.SYNOPSIS
Boots the full Nornis stack locally: SQL Server + Service Bus emulator in Docker,
migrations applied, then API + Worker + Web launched in separate windows.

The apps get connection strings pointed at the local containers via environment
variables, which override any cloud values in user secrets — so local runs can never
touch cloud SQL or compete with the cloud worker for queue messages. The Azure OpenAI
key still comes from user secrets (Extraction:AiApiKey), so extraction makes real AI
calls; everything else stays on your machine.

.PARAMETER InfraOnly
Start containers and apply migrations, but do not launch the apps.
#>
param(
    [switch]$InfraOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$sqlConn = 'Server=localhost,14330;Database=nornis-local;User ID=sa;Password=Nornis!LocalDev1;TrustServerCertificate=True'
$sbConn = 'Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;'

Write-Host '== Starting local infrastructure (SQL Server + Service Bus emulator)…'
docker compose -f (Join-Path $root 'compose.local.yaml') up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed' }

Write-Host '== Applying EF migrations to nornis-local…'
dotnet ef database update `
    --project (Join-Path $root 'src/Nornis.Infrastructure') `
    --startup-project (Join-Path $root 'src/Nornis.Infrastructure') `
    --connection $sqlConn
if ($LASTEXITCODE -ne 0) { throw 'migrations failed' }

if ($InfraOnly) {
    Write-Host '== Infra ready. SQL on localhost,14330 · Service Bus on localhost:5672'
    return
}

Write-Host '== Launching API (http://localhost:5000), Worker, Web (http://localhost:5100)…'

$apiEnv = @(
    "ConnectionStrings__DefaultConnection=$sqlConn",
    "AzureServiceBus__ConnectionString=$sbConn",
    'ASPNETCORE_ENVIRONMENT=Development',
    'ASPNETCORE_URLS=http://localhost:5000'
)
$workerEnv = @(
    "ConnectionStrings__DefaultConnection=$sqlConn",
    "ServiceBus__ConnectionString=$sbConn",
    'DOTNET_ENVIRONMENT=Development'
)
$webEnv = @(
    'Api__BaseUrl=http://localhost:5000',
    'ASPNETCORE_ENVIRONMENT=Development',
    'ASPNETCORE_URLS=http://localhost:5100'
)

function Start-App([string]$project, [string[]]$envVars, [string]$title) {
    $envSetup = ($envVars | ForEach-Object {
        $name, $value = $_ -split '=', 2
        "`$env:$name = '$($value -replace \"'\", \"''\")'"
    }) -join '; '
    Start-Process pwsh -ArgumentList @(
        '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = '$title'; $envSetup; dotnet run --project '$project'"
    )
}

Start-App (Join-Path $root 'src/Nornis.Api')    $apiEnv    'nornis-api (local)'
Start-App (Join-Path $root 'src/Nornis.Worker') $workerEnv 'nornis-worker (local)'
Start-App (Join-Path $root 'src/Nornis.Web')    $webEnv    'nornis-web (local)'

Write-Host '== All launched. Web: http://localhost:5100 · API: http://localhost:5000 (dev-auth bypass active)'
Write-Host '   Stop infra with: docker compose -f compose.local.yaml down'
