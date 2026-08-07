<#
.SYNOPSIS
Runs the test suite with coverage collection and builds a merged HTML report under
artifacts/coverage/report (gitignored).

Coverage in Nornis is signal, not a gate: this script never fails a run for being
under some number. It fails only when tests fail. See docs/plans/test-quality.md.

Test projects are run one at a time rather than via the solution, because the local
dev servers hold locks on the Api/Web test binaries. A locked project is reported as
skipped and the rest of the run continues — never kill the servers to get a number.

.PARAMETER Projects
Substrings matched against test project names, to subset the run.
`-Projects Domain,Application` runs those two.

.PARAMETER NoOpen
Do not open the report in a browser. Implied off-Windows and when $env:CI is set.

.EXAMPLE
./scripts/coverage.ps1
./scripts/coverage.ps1 -Projects Domain,Application
#>
param(
    [string[]]$Projects,
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root 'artifacts/coverage'
$rawDir = Join-Path $outDir 'raw'
$reportDir = Join-Path $outDir 'report'

$testProjects = Get-ChildItem (Join-Path $root 'tests') -Directory |
    ForEach-Object { Get-ChildItem $_.FullName -Filter '*.csproj' } |
    Sort-Object Name

if ($Projects) {
    $testProjects = $testProjects | Where-Object {
        $name = $_.BaseName
        $Projects | Where-Object { $name -like "*$_*" }
    }
    if (-not $testProjects) { throw "No test project matched: $($Projects -join ', ')" }
}

Write-Host "== Collecting coverage from $($testProjects.Count) test project(s)…"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item $rawDir -ItemType Directory -Force | Out-Null

$results = [ordered]@{}
foreach ($project in $testProjects) {
    $name = $project.BaseName
    Write-Host "-- $name"
    $output = & dotnet test --project $project.FullName `
        --coverage --coverage-output-format cobertura `
        --coverage-settings (Join-Path $root 'codecoverage.runsettings') `
        --results-directory (Join-Path $rawDir $name) 2>&1
    if ($LASTEXITCODE -eq 0) {
        $results[$name] = 'passed'
        continue
    }

    # MSB3021/MSB3027 on a test binary means a dev server has the assembly open.
    $locked = $output -match 'MSB302[17]|being used by another process'
    if ($locked) {
        $results[$name] = 'skipped (binaries locked by a running app)'
        Write-Warning "$name skipped — its binaries are locked, most likely by a local dev server."
        continue
    }

    $results[$name] = 'FAILED'
    $output | Write-Host
}

# One level down — each project reports into its own subdirectory. Not -Recurse: the
# coverage engine writes every cobertura a second time under <host>/In/<machine>/, and a
# recursive sweep hands ReportGenerator a duplicate of each project.
$coverageFiles = Get-ChildItem (Join-Path $rawDir '*') -Filter '*.cobertura.xml' -ErrorAction SilentlyContinue
if (-not $coverageFiles) { throw 'No coverage files produced — nothing to report.' }

Write-Host '== Merging into an HTML report…'
dotnet tool restore | Out-Null
# JsonSummary is what scripts/coverage-gate.ps1 and the CI history append both read. CI
# asked for it and this did not, so the gate could not be run on a dev machine at all —
# including its -Suggest mode, which exists precisely to be run by a person.
dotnet reportgenerator `
    "-reports:$(Join-Path $rawDir '*/*.cobertura.xml')" `
    "-targetdir:$reportDir" `
    '-reporttypes:Html;Cobertura;JsonSummary' `
    '-title:Nornis coverage' `
    -verbosity:Warning
if ($LASTEXITCODE -ne 0) { throw 'reportgenerator failed' }

Write-Host ''
foreach ($entry in $results.GetEnumerator()) {
    Write-Host ('   {0,-32} {1}' -f $entry.Key, $entry.Value)
}
$index = Join-Path $reportDir 'index.html'
Write-Host "`n== Report: $index"

if (-not $NoOpen -and -not $env:CI -and $IsWindows) { Start-Process $index }
if ($results.Values -contains 'FAILED') { exit 1 }
