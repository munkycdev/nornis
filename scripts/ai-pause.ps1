<#
.SYNOPSIS
Pause or resume every paid AI call across Nornis. Companion to
docs/runbooks/ai-paused.md.

Per-world budgets cap spend over a day; they cannot stop it now. This is the lever for a
provider incident, a runaway loop, or a bill climbing faster than anyone expected — the
alternative being a code change and a rollout, which needs a working pipeline at exactly
the moment things are already going wrong.

Deliberately a script and not a UI. A switch that pauses the product for everyone is one
nobody should be able to click by accident, and an operator flipping it is already at a
terminal.

.PARAMETER Action
Status (default, read-only), Pause, or Resume.

.PARAMETER Reason
Shown to users when an interactive path refuses, so a pause reads as deliberate rather
than broken. Required to pause — an unexplained outage is what this exists to avoid.

.PARAMETER ConnectionString
Overrides the connection string. Defaults to the API's user-secret, the same source the
migration commands use.

.EXAMPLE
./scripts/ai-pause.ps1
Show whether AI is paused, and why.

.EXAMPLE
./scripts/ai-pause.ps1 -Action Pause -Reason "Azure OpenAI incident, tracking DPS-1234"

.EXAMPLE
./scripts/ai-pause.ps1 -Action Resume

.NOTES
Takes effect within about ninety seconds: hosts cache the flag for a minute, and the
worker polls every twenty seconds. Interactive paths (Ask, assess) refuse immediately once
their cache turns over; the queue workers stop consuming, which leaves queued work waiting
in the queue rather than dead-lettering it.
#>
[CmdletBinding()]
param(
    [ValidateSet('Status', 'Pause', 'Resume')]
    [string]$Action = 'Status',

    [string]$Reason,

    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'

if ($Action -eq 'Pause' -and [string]::IsNullOrWhiteSpace($Reason)) {
    throw "Pausing needs -Reason. It is shown to users, and a pause nobody can explain is indistinguishable from a fault."
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $secret = dotnet user-secrets list --project src/Nornis.Api |
        Select-String '^ConnectionStrings:DefaultConnection'
    if (-not $secret) {
        throw "No connection string. Pass -ConnectionString, or set the API's user-secret."
    }
    $ConnectionString = $secret.ToString() -replace '^ConnectionStrings:DefaultConnection = ', ''
}

# Parsed rather than passed whole so the password never reaches a process argument list,
# where it would sit in the command history and in any process listing.
$parts = @{}
foreach ($pair in $ConnectionString -split ';') {
    if ($pair -match '=') {
        $kv = $pair -split '=', 2
        $parts[$kv[0].Trim()] = $kv[1].Trim()
    }
}

$server = if ($parts['Server']) { $parts['Server'] } else { $parts['Data Source'] }
$database = if ($parts['Initial Catalog']) { $parts['Initial Catalog'] } else { $parts['Database'] }
$user = if ($parts['User ID']) { $parts['User ID'] } else { $parts['UID'] }
$password = if ($parts['Password']) { $parts['Password'] } else { $parts['PWD'] }

$sqlcmd = Get-ChildItem "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe" -ErrorAction SilentlyContinue |
    Select-Object -Last 1
if (-not $sqlcmd) { $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue }
if (-not $sqlcmd) { throw "sqlcmd not found. Install the SQL Server command line utilities." }
$sqlcmdPath = if ($sqlcmd.FullName) { $sqlcmd.FullName } else { $sqlcmd.Source }

function Invoke-Sql([string]$Query) {
    & $sqlcmdPath -S $server -d $database -U $user -P $password -Q $Query -h -1 -W
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
}

# The row is upserted rather than inserted: one row per flag is the invariant, and a second
# row for 'ai-paused' would be a bug rather than a record.
switch ($Action) {
    'Pause' {
        $escaped = $Reason -replace "'", "''"
        Invoke-Sql @"
SET NOCOUNT ON;
MERGE OperationalFlags AS target
USING (SELECT 'ai-paused' AS Name) AS source ON target.Name = source.Name
WHEN MATCHED THEN UPDATE SET Enabled = 1, Reason = '$escaped', UpdatedAt = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN INSERT (Name, Enabled, Reason, UpdatedAt)
    VALUES ('ai-paused', 1, '$escaped', SYSDATETIMEOFFSET());
"@
        Write-Output "AI PAUSED: $Reason"
        Write-Output "Effective within ~90s. Queued work waits in the queue; nothing is dead-lettered."
    }
    'Resume' {
        Invoke-Sql @"
SET NOCOUNT ON;
UPDATE OperationalFlags SET Enabled = 0, Reason = NULL, UpdatedAt = SYSDATETIMEOFFSET()
WHERE Name = 'ai-paused';
"@
        Write-Output "AI resumed. Workers restart consuming within ~90s."
    }
    'Status' {
        Invoke-Sql @"
SET NOCOUNT ON;
SELECT CASE WHEN Enabled = 1 THEN 'PAUSED: ' + ISNULL(Reason, '(no reason recorded)')
            ELSE 'running' END + '  (updated ' + CONVERT(varchar, UpdatedAt, 120) + ')'
FROM OperationalFlags WHERE Name = 'ai-paused';
"@
        Write-Output "(no row means running — the flag has never been set)"
    }
}
