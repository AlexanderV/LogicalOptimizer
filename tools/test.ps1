<#
.SYNOPSIS
    Canonical test entry point for LogicalOptimizer.

.DESCRIPTION
    Default (no switches) runs the fast gate — the same filter CI uses
    (Category!=Performance&Category!=Exhaustive), ~1370 tests in well under a minute.
    This is the loop to run while developing.

    The expensive categories are opt-in and each runs with xUnit collection
    parallelism DISABLED (-- xUnit.ParallelizeTestCollections=false): the exhaustive
    sweeps are CPU-bound whole-function-space enumerations, and running five of them
    concurrently makes an otherwise-healthy run look like a hang. Any test running
    past 60 s is named in the console output (xunit.runner.json longRunningTestSeconds),
    so a long sweep reads as progress, not as a freeze.

.PARAMETER Performance
    Run only Category=Performance (timing-sensitive suites; ~minutes).

.PARAMETER Exhaustive
    Run only Category=Exhaustive (whole-function-space sweeps; ~20-40 minutes,
    sequential on purpose).

.PARAMETER Full
    Everything: fast gate, then Performance, then Exhaustive — each as its own run so
    the expensive categories never compete with the gate or with each other.

.PARAMETER NoBuild
    Skip the up-front build (use when the Release binaries are already current).

.EXAMPLE
    pwsh tools/test.ps1              # fast gate — the everyday loop
    pwsh tools/test.ps1 -Exhaustive  # the sweeps, sequential
    pwsh tools/test.ps1 -Full        # everything, in the right order
#>
[CmdletBinding()]
param(
    [switch]$Performance,
    [switch]$Exhaustive,
    [switch]$Full,
    [switch]$NoBuild,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path (Split-Path $PSScriptRoot -Parent) 'LogicalOptimizer.sln'

function Invoke-TestRun {
    param(
        [string]$Label,
        [string]$Filter,
        [switch]$Serialize
    )
    Write-Host ""
    Write-Host "== $Label ==" -ForegroundColor Cyan
    $testArgs = @('test', $solution, '--configuration', $Configuration, '--no-build',
        '--filter', $Filter)
    if ($Serialize) {
        # DiagnosticMessages + verbosity=normal make the xUnit diagnostics visible: the
        # parallelism banner and the long-running-test lines that name whichever sweep is
        # currently working (xunit.runner.json's longRunningTestSeconds). Enabled only for
        # the expensive runs — in the fast gate the same diagnostics are just noise.
        $testArgs += @('--logger', 'console;verbosity=normal',
            '--', 'xUnit.ParallelizeTestCollections=false', 'xUnit.DiagnosticMessages=true')
    }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "'$Label' failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

if (-not $NoBuild) {
    & dotnet build $solution --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$fastFilter = 'Category!=Performance&Category!=Exhaustive'

if ($Full) {
    Invoke-TestRun -Label 'Fast gate (CI filter)' -Filter $fastFilter
    Invoke-TestRun -Label 'Performance category' -Filter 'Category=Performance' -Serialize
    Invoke-TestRun -Label 'Exhaustive category (sequential; expect ~20-40 min)' `
        -Filter 'Category=Exhaustive' -Serialize
}
elseif ($Exhaustive) {
    Invoke-TestRun -Label 'Exhaustive category (sequential; expect ~20-40 min)' `
        -Filter 'Category=Exhaustive' -Serialize
}
elseif ($Performance) {
    Invoke-TestRun -Label 'Performance category' -Filter 'Category=Performance' -Serialize
}
else {
    Invoke-TestRun -Label 'Fast gate (CI filter)' -Filter $fastFilter
}

Write-Host ""
Write-Host "All requested test runs passed." -ForegroundColor Green
