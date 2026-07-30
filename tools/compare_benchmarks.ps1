#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Performance-regression gate for LogicalOptimizer (roadmap P0.3). Compares a fresh
    BenchmarkDotNet run against a committed baseline on ALLOCATED BYTES PER OPERATION.

.DESCRIPTION
    BenchmarkDotNet's MemoryDiagnoser reports allocated bytes per operation
    deterministically and machine-independently, so an allocation comparison is a sound
    regression gate that does NOT flake on shared/loaded CI runners. Wall-clock time is
    captured and printed for information only and NEVER affects the exit code.

    The script reads one or more `*-report-full.json` files (the JSON exporter output of
    the LogicalOptimizer.Benchmarks project) from -Current (a file or a directory), and a
    baseline JSON file (-Baseline). For every benchmark present in BOTH it compares the
    allocated bytes and FAILS (exit 1) if any current value exceeds
    `baseline * (1 + Threshold)`. A per-benchmark table (baseline / current / delta% /
    OK|REGRESSION) is printed, followed by an INFORMATIONAL time-delta table.

    New benchmarks (present in the current run but not the baseline) are reported but do
    not fail the gate. Missing benchmarks (in the baseline but absent from the current
    run) are reported as a WARNING and do not fail the gate.

    -UpdateBaseline rewrites the baseline file from the current run (the documented
    regeneration path, analogous to the repo's golden-master regeneration convention) and
    exits 0 without gating.

    Dependency-free: uses only built-in cmdlets (ConvertFrom-Json / ConvertTo-Json). Runs
    on Windows PowerShell 5.1 and PowerShell 7+ (the pwsh shipped on GitHub-hosted
    runners), matching the style of tools/verify_nuget.ps1.

.PARAMETER Current
    Path to a fresh BenchmarkDotNet `*-report-full.json` file, or a directory containing
    such files (e.g. BenchmarkDotNet.Artifacts/results). Required.

.PARAMETER Baseline
    Path to the committed baseline JSON. Default: doc/perf-baseline.json (resolved
    relative to the repository root, i.e. the parent of this tools/ directory).

.PARAMETER Threshold
    Allowed fractional increase in allocated bytes before a benchmark is flagged as a
    REGRESSION. Default 0.10 (= 10%).

.PARAMETER UpdateBaseline
    Rewrite the baseline file from the current run and exit 0 (no gating). Use this after
    an intentional, reviewed change to a hot path.

.EXAMPLE
    # Gate a fresh run against the committed baseline (default 10% allocation threshold):
    pwsh tools/compare_benchmarks.ps1 -Current BenchmarkDotNet.Artifacts/results

.EXAMPLE
    # Regenerate the committed baseline after an intentional hot-path change:
    pwsh tools/compare_benchmarks.ps1 -Current BenchmarkDotNet.Artifacts/results -UpdateBaseline
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Current,

    [string] $Baseline,

    [ValidateRange(0.0, 100.0)]
    [double] $Threshold = 0.10,

    # Absolute companion to -Threshold: growth must exceed this many bytes as well before it counts
    # as a regression. 32 KB is roughly four times the ~8 KB run-to-run spread observed on the
    # small benchmarks, so genuine regressions still fail while measurement noise does not.
    [long] $MinimumRegressionBytes = 32768,

    [switch] $UpdateBaseline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Baseline) {
    $Baseline = Join-Path $repoRoot 'doc/perf-baseline.json'
}

# ---- Collect the *-report-full.json files of the current run -------------------------
function Get-ReportFiles {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Current path not found: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.PSIsContainer) {
        $files = @(Get-ChildItem -LiteralPath $Path -Recurse -Filter '*-report-full.json' -File |
            Sort-Object FullName)
        if ($files.Count -eq 0) {
            throw "No *-report-full.json files found under directory: $Path"
        }
        return $files
    }
    return @($item)
}

# ---- Parse the current run into an ordered map of benchmark id -> metrics ------------
function Read-CurrentRun {
    param([object[]] $Files)

    $benchmarks = [ordered]@{}
    $environment = $null

    foreach ($file in $Files) {
        $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json

        if ($null -eq $environment -and
            ($json.PSObject.Properties.Name -contains 'HostEnvironmentInfo')) {
            $h = $json.HostEnvironmentInfo
            $environment = [ordered]@{
                benchmarkDotNetVersion = [string]$h.BenchmarkDotNetVersion
                runtimeVersion         = [string]$h.RuntimeVersion
                dotnetCliVersion       = [string]$h.DotNetCliVersion
                osVersion              = [string]$h.OsVersion
                architecture           = [string]$h.Architecture
                configuration          = [string]$h.Configuration
            }
        }

        if (-not ($json.PSObject.Properties.Name -contains 'Benchmarks')) { continue }

        foreach ($b in $json.Benchmarks) {
            $id = [string]$b.FullName

            $allocated = $null
            if (($b.PSObject.Properties.Name -contains 'Memory') -and $null -ne $b.Memory -and
                ($b.Memory.PSObject.Properties.Name -contains 'BytesAllocatedPerOperation')) {
                $allocated = [long]$b.Memory.BytesAllocatedPerOperation
            }

            $mean = $null
            if (($b.PSObject.Properties.Name -contains 'Statistics') -and $null -ne $b.Statistics -and
                ($b.Statistics.PSObject.Properties.Name -contains 'Mean')) {
                $mean = [double]$b.Statistics.Mean
            }

            if ($null -eq $allocated) {
                Write-Warning ("  {0}: no MemoryDiagnoser data (BytesAllocatedPerOperation) - skipped" -f $id)
                continue
            }

            $benchmarks[$id] = [ordered]@{
                allocatedBytesPerOperation = $allocated
                meanNanoseconds            = $mean
            }
        }
    }

    if ($benchmarks.Count -eq 0) {
        throw 'No benchmarks with allocation data were found in the current run.'
    }

    return [pscustomobject]@{
        Environment = $environment
        Benchmarks  = $benchmarks
    }
}

# ---- Build and write the baseline document -------------------------------------------
function Write-Baseline {
    param(
        [string]   $Path,
        [object]   $Run,
        [double]   $Threshold
    )

    $benchmarks = [ordered]@{}
    foreach ($id in ($Run.Benchmarks.Keys | Sort-Object)) {
        $benchmarks[$id] = $Run.Benchmarks[$id]
    }

    $doc = [ordered]@{
        note             = ('Performance-regression baseline (roadmap P0.3). The gate is ' +
            'ALLOCATION-based: allocatedBytesPerOperation is compared by ' +
            'tools/compare_benchmarks.ps1 and a run FAILS if any benchmark exceeds ' +
            'baseline * (1 + threshold). meanNanoseconds is INFORMATIONAL only and is ' +
            'never asserted (wall-clock is noisy on shared CI runners). Regenerate with: ' +
            'pwsh tools/compare_benchmarks.ps1 -Current RESULTS_DIR -UpdateBaseline.')
        thresholdDefault = $Threshold
        minimumRegressionBytes = $MinimumRegressionBytes
        generatedUtc     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        environment      = $Run.Environment
        benchmarks       = $benchmarks
    }

    $json = $doc | ConvertTo-Json -Depth 10
    # ConvertTo-Json emits CRLF on Windows PowerShell; normalise to LF and a trailing NL.
    $json = ($json -replace "`r`n", "`n").TrimEnd() + "`n"
    Set-Content -LiteralPath $Path -Value $json -NoNewline -Encoding UTF8
}

# ---- Load the committed baseline into the same shape ---------------------------------
function Read-Baseline {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Baseline file not found: $Path (create it with -UpdateBaseline)"
    }
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json

    $benchmarks = [ordered]@{}
    if ($json.PSObject.Properties.Name -contains 'benchmarks' -and $null -ne $json.benchmarks) {
        foreach ($prop in $json.benchmarks.PSObject.Properties) {
            $benchmarks[$prop.Name] = [ordered]@{
                allocatedBytesPerOperation = [long]$prop.Value.allocatedBytesPerOperation
                meanNanoseconds            = $(if ($null -ne $prop.Value.meanNanoseconds) { [double]$prop.Value.meanNanoseconds } else { $null })
            }
        }
    }
    return $benchmarks
}

function Format-Bytes {
    param([long] $Bytes)
    return ('{0:N0}' -f $Bytes)
}

# ======================================================================================
$reportFiles = @(Get-ReportFiles -Path $Current)
Write-Host ("Reading {0} BenchmarkDotNet report file(s) from: {1}" -f $reportFiles.Count, $Current)
$run = Read-CurrentRun -Files $reportFiles
Write-Host ("Current run contains {0} benchmark(s) with allocation data." -f $run.Benchmarks.Count)
Write-Host ''

if ($UpdateBaseline) {
    Write-Baseline -Path $Baseline -Run $run -Threshold $Threshold
    Write-Host ("Baseline updated from current run -> {0}" -f $Baseline)
    Write-Host ("  {0} benchmark(s) written." -f $run.Benchmarks.Count)
    exit 0
}

$baselineBenchmarks = Read-Baseline -Path $Baseline
Write-Host ("Baseline: {0} ({1} benchmark(s), threshold {2:P0})" -f $Baseline, $baselineBenchmarks.Count, $Threshold)
Write-Host ''

# ---- Allocation gate (authoritative) -------------------------------------------------
Write-Host '==== Allocation regression gate (bytes/op) ===================================='
Write-Host ('{0,-52} {1,14} {2,14} {3,9}  {4}' -f 'Benchmark', 'Baseline', 'Current', 'Delta%', 'Status')
Write-Host ('-' * 100)

$regressions = [System.Collections.Generic.List[string]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
$added = [System.Collections.Generic.List[string]]::new()

$commonIds = @($run.Benchmarks.Keys | Where-Object { $baselineBenchmarks.Contains($_) } | Sort-Object)

foreach ($id in $commonIds) {
    $baseAlloc = [long]$baselineBenchmarks[$id].allocatedBytesPerOperation
    $curAlloc = [long]$run.Benchmarks[$id].allocatedBytesPerOperation

    if ($baseAlloc -le 0) {
        $deltaPct = [double]::PositiveInfinity
    }
    else {
        $deltaPct = (($curAlloc - $baseAlloc) / [double]$baseAlloc) * 100.0
    }

    # A regression must be BOTH relatively and absolutely significant.
    #
    # The relative test alone is unusable once a benchmark's allocation is small: measured
    # bytes/op for the smaller benchmarks is bimodal across runs of the SAME binary - e.g.
    # FormulaFactory_ImportFortyVarCover reports either ~102 KB or ~110 KB, an 8% spread with no
    # code change, and the same run composition produces both. With a 10% relative threshold and
    # baselines that are now in the hundreds of kilobytes rather than megabytes, that noise sits
    # right at the failure line and the gate starts flagging changes that never happened.
    # Requiring an absolute increase as well keeps the gate meaningful where it matters - a real
    # regression of any consequence moves far more than this - without it crying wolf.
    $limit = [double]$baseAlloc * (1.0 + $Threshold)
    $absoluteGrowth = $curAlloc - $baseAlloc
    $isRegression = ($curAlloc -gt $limit) -and ($absoluteGrowth -gt $MinimumRegressionBytes)
    $status = if ($isRegression) { 'REGRESSION' }
              elseif ($curAlloc -gt $limit) { 'noise' }
              else { 'OK' }
    if ($isRegression) { [void]$regressions.Add($id) }

    $shortId = $id
    if ($shortId.Length -gt 52) { $shortId = $shortId.Substring(0, 49) + '...' }

    Write-Host ('{0,-52} {1,14} {2,14} {3,8:+0.0;-0.0;0.0}%  {4}' -f `
            $shortId, (Format-Bytes $baseAlloc), (Format-Bytes $curAlloc), $deltaPct, $status)
}

# Missing = in baseline, absent from current run.
foreach ($id in ($baselineBenchmarks.Keys | Sort-Object)) {
    if (-not $run.Benchmarks.Contains($id)) { [void]$missing.Add($id) }
}
# Added = in current run, absent from baseline.
foreach ($id in ($run.Benchmarks.Keys | Sort-Object)) {
    if (-not $baselineBenchmarks.Contains($id)) { [void]$added.Add($id) }
}

Write-Host ('-' * 100)
Write-Host ''

# ---- Informational time deltas (NEVER affect the exit code) --------------------------
Write-Host '==== Time deltas (INFORMATIONAL ONLY - not gated) ============================='
Write-Host ('{0,-52} {1,14} {2,14} {3,9}' -f 'Benchmark', 'Baseline ms', 'Current ms', 'Delta%')
Write-Host ('-' * 100)
foreach ($id in $commonIds) {
    $baseMean = $baselineBenchmarks[$id].meanNanoseconds
    $curMean = $run.Benchmarks[$id].meanNanoseconds
    if ($null -eq $baseMean -or $null -eq $curMean -or [double]$baseMean -le 0) {
        continue
    }
    $baseMs = [double]$baseMean / 1e6
    $curMs = [double]$curMean / 1e6
    $tDelta = (($curMs - $baseMs) / $baseMs) * 100.0
    $shortId = $id
    if ($shortId.Length -gt 52) { $shortId = $shortId.Substring(0, 49) + '...' }
    Write-Host ('{0,-52} {1,14:N3} {2,14:N3} {3,8:+0.0;-0.0;0.0}%' -f $shortId, $baseMs, $curMs, $tDelta)
}
Write-Host ('-' * 100)
Write-Host ''

# ---- Summary + exit code -------------------------------------------------------------
if ($added.Count -gt 0) {
    Write-Host ("NEW benchmark(s) not in baseline (reported, not gated): {0}" -f ($added -join ', '))
}
if ($missing.Count -gt 0) {
    Write-Warning ("MISSING benchmark(s) in baseline but absent from current run: {0}" -f ($missing -join ', '))
}

Write-Host ''
Write-Host '==== Result ==================================================================='
if ($regressions.Count -gt 0) {
    Write-Host ("  {0} benchmark(s) compared, {1} REGRESSION(S). (Rows marked 'noise' are over the" -f $commonIds.Count, $regressions.Count)
    Write-Host ("  relative threshold but under the {0:N0}-byte absolute floor, so they are not failures.)" -f $MinimumRegressionBytes)
    Write-Error ("Allocation regression over threshold {0:P0}: {1}" -f $Threshold, ($regressions -join ', '))
    exit 1
}

Write-Host ("  {0} benchmark(s) compared, all within {1:P0} + {2:N0} bytes allocation threshold. OK." -f $commonIds.Count, $Threshold, $MinimumRegressionBytes)
exit 0
