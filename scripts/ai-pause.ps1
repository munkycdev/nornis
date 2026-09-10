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

Authenticates as you. The work happens in scripts/ai-pause.cs, which mints a SQL token
from your own `az login` and hands it to SqlClient — there is no password to read, parse
or pass, because since O3 (2026-09-08) nothing reaches nornis-db with one. You need a
contained user in the database, or to be the server's Entra admin. This wrapper exists
so the documented invocation is one line from any directory; `dotnet run
scripts/ai-pause.cs -- status` from the repo root is the same thing.

.PARAMETER Action
Status (default, read-only), Pause, or Resume.

.PARAMETER Reason
Shown to users when an interactive path refuses, so a pause reads as deliberate rather
than broken. Required to pause — an unexplained outage is what this exists to avoid.

.PARAMETER Server
The SQL server host. Defaults to the live one.

.PARAMETER Database
The database name. Defaults to the live one.

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

    [string]$Server = 'sql-chronicis-dev.database.windows.net',

    [string]$Database = 'nornis-db'
)

$ErrorActionPreference = 'Stop'

# The -Reason rule is enforced in ai-pause.cs, the one place it lives; this wrapper only
# forwards. Checking here too would save a compile on the failure path at the cost of two
# copies of the same sentence.
$arguments = @($Action.ToLowerInvariant())
if ($Action -eq 'Pause') { $arguments += $Reason }

# Env, not argv, for the target: it is what the sibling sql-identity-users.cs honours. Restored
# afterward because a script runs in the caller's session and would otherwise leave them set.
$previousServer = $env:SQL_SERVER
$previousDatabase = $env:SQL_DATABASE
try {
    $env:SQL_SERVER = $Server
    $env:SQL_DATABASE = $Database
    dotnet run (Join-Path $PSScriptRoot 'ai-pause.cs') -- @arguments
    if ($LASTEXITCODE -ne 0) { throw "ai-pause.cs failed with exit code $LASTEXITCODE." }
}
finally {
    $env:SQL_SERVER = $previousServer
    $env:SQL_DATABASE = $previousDatabase
}
