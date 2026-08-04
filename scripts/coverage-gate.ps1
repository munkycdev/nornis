<#
.SYNOPSIS
Fails the build when a named assembly drops below its coverage floor.

Floors live in coverage-thresholds.json, one per assembly, line and branch separately. A
null floor is not enforced — the assembly is reported and passed — so turning the gate on
for an assembly is editing a number and nothing else.

Two things this deliberately does not do. There is no solution-wide aggregate floor: an
aggregate lets a collapse in one assembly hide behind a rise in another. And there are no
floors on Api, Web, Worker or Infrastructure, whose coverage is dominated by wiring —
report-only unless that is revisited in writing.

The failure mode worth guarding is a gate that passes because it found nothing to check.
An assembly named in the thresholds file but missing from the report is a failure here,
not a skip: a rename or a dropped test project would otherwise silently switch the gate
off and leave a green tick behind.

.PARAMETER SummaryPath
ReportGenerator's Summary.json. Defaults to what scripts/coverage.ps1 produces. The same
file the coverage-history append reads, so the gate and the chart cannot disagree.

.PARAMETER ThresholdsPath
Defaults to coverage-thresholds.json at the repo root.

.PARAMETER Suggest
Report only, exit 0 whatever happens, and print what each floor would be if set a couple
of points under what was just observed. For deciding numbers, not for CI.

.EXAMPLE
./scripts/coverage.ps1 -NoOpen
./scripts/coverage-gate.ps1

.EXAMPLE
./scripts/coverage-gate.ps1 -Suggest
#>
param(
    [string]$SummaryPath,
    [string]$ThresholdsPath,
    [switch]$Suggest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $SummaryPath) {
    $SummaryPath = Join-Path $root 'artifacts/coverage/report/Summary.json'
}
if (-not $ThresholdsPath) {
    $ThresholdsPath = Join-Path $root 'coverage-thresholds.json'
}

if (-not (Test-Path $SummaryPath)) {
    throw "No coverage summary at '$SummaryPath'. Run ./scripts/coverage.ps1 first, or pass -SummaryPath."
}
if (-not (Test-Path $ThresholdsPath)) {
    throw "No thresholds file at '$ThresholdsPath'."
}

$summary = Get-Content $SummaryPath -Raw | ConvertFrom-Json
$thresholds = Get-Content $ThresholdsPath -Raw | ConvertFrom-Json

# ReportGenerator names them .coverage (line) and .branchcoverage; keep the mapping in one
# place so the rest of this reads in the vocabulary the thresholds file uses.
$observed = @{}
foreach ($assembly in $summary.coverage.assemblies) {
    $observed[$assembly.name] = [pscustomobject]@{
        Line   = [double]$assembly.coverage
        Branch = [double]$assembly.branchcoverage
    }
}

# How far under the observed value a suggested floor sits. Enough to absorb the ordinary
# drift of a PR that adds a little uncovered wiring, not enough to hide a real regression.
$suggestMargin = 2.0

$rows = @()
$failures = @()
$missing = @()

foreach ($name in $thresholds.assemblies.PSObject.Properties.Name) {
    $floors = $thresholds.assemblies.$name

    if (-not $observed.ContainsKey($name)) {
        $missing += $name
        continue
    }

    $actual = $observed[$name]

    foreach ($metric in @('line', 'branch')) {
        $floor = $floors.$metric
        $value = if ($metric -eq 'line') { $actual.Line } else { $actual.Branch }

        $state = if ($null -eq $floor) {
            'not set'
        }
        elseif ($value -lt $floor) {
            $failures += "$name $metric coverage is $($value.ToString('0.0'))%, below its floor of $floor%"
            'BELOW FLOOR'
        }
        else {
            'ok'
        }

        $rows += [pscustomobject]@{
            Assembly  = $name
            Metric    = $metric
            Observed  = $value.ToString('0.0')
            Floor     = if ($null -eq $floor) { '—' } else { $floor.ToString() }
            State     = $state
            Suggested = [Math]::Floor(($value - $suggestMargin) * 10) / 10
        }
    }
}

if ($Suggest) {
    $rows | Format-Table Assembly, Metric, Observed, Floor, Suggested -AutoSize
    Write-Host "Suggested floors sit $suggestMargin points under what was just observed."
    Write-Host "One run is one data point — check history.json on the coverage-history branch before committing a number."
    exit 0
}

$rows | Format-Table Assembly, Metric, Observed, Floor, State -AutoSize

if ($missing.Count -gt 0) {
    Write-Host "::error::Coverage thresholds name assemblies the report does not contain: $($missing -join ', '). Either they were renamed, or their tests stopped running — both would have left this gate passing on nothing."
    exit 1
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "::error::$failure"
    }
    Write-Host "Floors are in coverage-thresholds.json. Ratcheting one down needs a written why in the same commit."
    exit 1
}

$enforced = @($rows | Where-Object { $_.State -ne 'not set' }).Count
if ($enforced -eq 0) {
    Write-Host "No floors are set yet — every assembly above is report-only. See _timing in coverage-thresholds.json."
}
else {
    Write-Host "$enforced floor(s) met."
}
