<#
.SYNOPSIS
Ranks methods by CRAP score — complex code with poor coverage, which is the code that is
dangerous to change.

    CRAP(m) = complexity(m)^2 x (1 - coverage(m))^3 + complexity(m)

The shape is the argument. Coverage enters cubed, so covering a gnarly method collapses
its score fast; complexity enters squared, so a simple uncovered method never climbs. A
fully covered method scores exactly its complexity — the floor you cannot test away, only
refactor away.

This is the piece a blanket coverage gate cannot give you: it separates an uncovered
visibility branch from an uncovered DTO mapper, which a percentage treats identically.
The output is a test-writing backlog ordered by risk.

Reads ReportGenerator's merged Cobertura rather than raw per-project files: merging is
what makes a method covered by one test project and touched by another read correctly,
and ReportGenerator also resolves async state machines back to their real method names.

.PARAMETER CoberturaPath
Merged Cobertura. Defaults to what scripts/coverage.ps1 produces.

.PARAMETER Top
How many rows to report. Default 30.

.EXAMPLE
./scripts/coverage.ps1 -NoOpen
./scripts/crap-report.ps1
#>
param(
    [string]$CoberturaPath,
    [int]$Top = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root 'artifacts/coverage'

if (-not $CoberturaPath) {
    $CoberturaPath = Join-Path $outDir 'report/Cobertura.xml'
}

if (-not (Test-Path $CoberturaPath)) {
    throw "No merged Cobertura at $CoberturaPath. Run ./scripts/coverage.ps1 first."
}

# Amber and red, per the plan. A method at or above Red is one nobody should change
# casually; Amber is the queue behind it.
$AmberThreshold = 15
$RedThreshold = 30

Write-Host "== Scoring methods from $CoberturaPath…"
[xml]$report = Get-Content $CoberturaPath

# BuildRenderTree is what the Razor compiler emits for a component's markup. It is
# enormous, uncovered, and nobody wrote it — left in, it takes the entire top of the table
# (SourceDetail alone scores 4970) and buries the hand-written methods this list exists to
# surface. Coverlet's filters are assembly- and type-scoped, so it cannot be excluded from
# collection without also dropping the @code block in the same file; excluding it here is
# the only place the distinction can be made.
$GeneratedMethods = @('BuildRenderTree')

$methods = foreach ($class in $report.SelectNodes('//class')) {
    $className = $class.name
    foreach ($method in $class.SelectNodes('./methods/method')) {
        if ($GeneratedMethods -contains $method.name) { continue }

        $complexity = [double]$method.complexity
        $coverage = [double]$method.'line-rate'

        # Signature dropped deliberately: overloads are rare here and the full parameter
        # list makes the table unreadable at a glance, which is the only thing it is for.
        [pscustomobject]@{
            Method     = "$className.$($method.name)"
            Complexity = [int]$complexity
            Coverage   = [math]::Round($coverage * 100, 1)
            Crap       = [math]::Round(($complexity * $complexity) * [math]::Pow(1 - $coverage, 3) + $complexity, 1)
        }
    }
}

$ranked = $methods | Sort-Object -Property Crap -Descending | Select-Object -First $Top
$red = @($methods | Where-Object { $_.Crap -ge $RedThreshold }).Count
$amber = @($methods | Where-Object { $_.Crap -ge $AmberThreshold -and $_.Crap -lt $RedThreshold }).Count

New-Item $outDir -ItemType Directory -Force | Out-Null

# JSON for the dashboard; markdown for the step summary and for reading here.
[pscustomobject]@{
    generatedOn = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    thresholds  = @{ amber = $AmberThreshold; red = $RedThreshold }
    totals      = @{ methods = $methods.Count; red = $red; amber = $amber }
    hotspots    = @($ranked)
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outDir 'crap.json') -Encoding utf8

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine("## CRAP hotspots")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("$($methods.Count) methods · **$red** at or above $RedThreshold (red) · **$amber** at or above $AmberThreshold (amber)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Method | Complexity | Line coverage | CRAP |')
[void]$markdown.AppendLine('| --- | ---: | ---: | ---: |')
foreach ($m in $ranked) {
    $mark = if ($m.Crap -ge $RedThreshold) { '🔴' } elseif ($m.Crap -ge $AmberThreshold) { '🟠' } else { '' }
    [void]$markdown.AppendLine("| ``$($m.Method)`` | $($m.Complexity) | $($m.Coverage)% | $mark $($m.Crap) |")
}
$markdown.ToString() | Set-Content (Join-Path $outDir 'crap.md') -Encoding utf8

$ranked | Select-Object -First 15 | Format-Table -AutoSize
Write-Host "== $($methods.Count) methods scored · $red red · $amber amber"
Write-Host "== Wrote $(Join-Path $outDir 'crap.json') and crap.md"
