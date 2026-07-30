#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verify that a comparison run really reproduced the published report, and write a
    machine-readable reproduction report.

.DESCRIPTION
    doc/COMPARISON_METHODOLOGY.md documents a two-command reproduction (build the pinned container,
    run it). Nothing checked that the run SUCCEEDED in any meaningful sense: every competitor adapter
    is best-effort by design (an absent tool self-skips and leaves its cell `pending`, and nothing is
    ever fabricated), so the container can exit 0 having produced an all-`pending` report. An
    external reproducer would then have no way to tell "the tools disagree" from "the image is
    broken" - which makes "it reproduced" unfalsifiable.

    This script closes that hole. Against a freshly produced doc/comparison directory it asserts:

      * the required artifacts exist (our-results.json, manifest.json, summary.md, merged.md);
      * the SAME corpus was used - the sha256 recorded in the results matches the committed corpus
        file, byte for byte;
      * environment provenance is recorded (runtime, OS, architecture, CPU policy);
      * correctness holds INDEPENDENTLY of timing - every optimization/minimization row is
        `equivalent`, every equivalence miter is `unsat`, and the two independent counting engines
        agree on every model count;
      * the competitor side actually ran - at least one competitor column is populated, so an
        all-`pending` report fails instead of passing quietly;
      * optionally (-CompareWith), that non-timing fields are byte-identical to a previously
        committed run, which is the determinism claim the methodology makes but never checked.

    Timing is never asserted: wall-clock is machine-dependent and is only ever reported.

.PARAMETER ComparisonPath
    Directory holding the run's artifacts. Default 'doc/comparison'.

.PARAMETER CorpusPath
    The committed corpus the run must have used. Default 'tools/comparison_corpus.txt'.

.PARAMETER ReportPath
    Where to write the JSON reproduction report. Default 'comparison-reproduction-report.json'.

.PARAMETER CompareWith
    A previously committed our-results.json to diff non-timing fields against (determinism check).

.PARAMETER RequireCompetitors
    Minimum number of competitor columns that must be populated. Default 1 - raise it on a runner
    where every tool is expected to be present.

.EXAMPLE
    docker build -t logicopt-p0p2 tools/comparison
    docker run --rm -v "$PWD:/work" logicopt-p0p2
    pwsh tools/verify_comparison_reproduction.ps1

.EXAMPLE
    # Also assert the run is deterministic against what is committed:
    pwsh tools/verify_comparison_reproduction.ps1 -CompareWith committed/our-results.json
#>
[CmdletBinding()]
param(
    [string] $ComparisonPath = 'doc/comparison',
    [string] $CorpusPath = 'tools/comparison_corpus.txt',
    [string] $ReportPath = 'comparison-reproduction-report.json',
    [string] $CompareWith,
    [ValidateRange(0, 10)]
    [int] $RequireCompetitors = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [bool] $Ok,
        [string] $Detail = ''
    )
    $checks.Add([ordered]@{
        name   = $Name
        status = $(if ($Ok) { 'pass' } else { 'fail' })
        detail = $Detail
    })
    if ($Ok) { Write-Host ("  ok   {0}: {1}" -f $Name, $Detail) }
    else { Write-Host ("  FAIL {0}: {1}" -f $Name, $Detail) }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Content
    )
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path (Get-Location).Path $Path
    }
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host ("Verifying comparison reproduction in {0}" -f $ComparisonPath)
Write-Host ''

# --- artifacts present -------------------------------------------------------------------------

$required = @('our-results.json', 'manifest.json', 'summary.md', 'merged.md')
$missing = @($required | Where-Object { -not (Test-Path (Join-Path $ComparisonPath $_)) })
Add-Check -Name 'required-artifacts' -Ok ($missing.Count -eq 0) `
    -Detail $(if ($missing.Count -eq 0) { "all present: $($required -join ', ')" }
              else { "missing: $($missing -join ', ')" })

if ($missing.Count -gt 0) {
    Write-Utf8NoBom -Path $ReportPath -Content (([ordered]@{
        reportVersion = 1
        tool          = 'tools/verify_comparison_reproduction.ps1'
        summary       = [ordered]@{ checks = $checks.Count; failed = 1; result = 'fail' }
        checks        = @($checks)
    }) | ConvertTo-Json -Depth 6)
    Write-Error 'Reproduction incomplete: required artifacts are missing.'
    exit 1
}

$results = Get-Content -Raw (Join-Path $ComparisonPath 'our-results.json') | ConvertFrom-Json
$manifest = Get-Content -Raw (Join-Path $ComparisonPath 'manifest.json') | ConvertFrom-Json
$merged = Get-Content -Raw (Join-Path $ComparisonPath 'merged.md')

# --- the same corpus ---------------------------------------------------------------------------

if (Test-Path $CorpusPath) {
    # Must match how the harness computes it (CrossLibraryComparisonHarness.Sha256OfFile): the
    # digest is over LF-NORMALIZED UTF-8 text, deliberately, so it is identical whatever the
    # checkout's line-ending policy is. Hashing raw bytes here would fail on a CRLF checkout and
    # report a corpus mismatch that does not exist.
    $normalized = (Get-Content -Raw $CorpusPath) -replace "`r`n", "`n"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $actualHash = [System.BitConverter]::ToString(
            $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized))
        ).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
    $recordedHash = ([string]$results.corpus.sha256).ToLowerInvariant()
    Add-Check -Name 'corpus-is-the-committed-one' -Ok ($actualHash -eq $recordedHash) `
        -Detail $(if ($actualHash -eq $recordedHash) { "sha256 $recordedHash over $($results.corpus.functionCount) function(s)" }
                  else { "results recorded $recordedHash but $CorpusPath hashes to $actualHash" })
}
else {
    Add-Check -Name 'corpus-is-the-committed-one' -Ok $false -Detail "corpus not found at $CorpusPath"
}

# --- environment provenance --------------------------------------------------------------------

$provenanceFields = @('dotnetVersion', 'runtimeIdentifier', 'osDescription', 'architecture',
    'processorCount', 'cpuPolicy')
$blank = @($provenanceFields | Where-Object {
    -not ($manifest.PSObject.Properties.Name -contains $_) -or
    [string]::IsNullOrWhiteSpace([string]$manifest.$_)
})
Add-Check -Name 'environment-recorded' -Ok ($blank.Count -eq 0) `
    -Detail $(if ($blank.Count -eq 0) {
            "$($manifest.runtimeIdentifier) on $($manifest.osDescription) / $($manifest.architecture), $($manifest.processorCount) cpu(s)"
        } else {
            "manifest.json is missing or blank for: $($blank -join ', ')"
        })

# --- correctness, independent of any timing ----------------------------------------------------
# These are the claims the comparison is allowed to make. Timing is reported, never asserted.

foreach ($table in @('symbolicOptimization', 'twoLevelMinimization')) {
    $rows = @($results.$table)
    $bad = @($rows | Where-Object { [string]$_.equivalence -ne 'equivalent' } |
        ForEach-Object { "$($_.name)=$($_.equivalence)" })
    Add-Check -Name "$table-every-row-equivalent" -Ok ($bad.Count -eq 0) `
        -Detail $(if ($bad.Count -eq 0) { "$($rows.Count) row(s) verified equivalent" }
                  else { "not equivalent: $($bad -join ', ')" })
}

$satRows = @($results.sat)
$notUnsat = @($satRows | Where-Object { [string]$_.verdict -ne 'unsat' } |
    ForEach-Object { "$($_.name)=$($_.verdict)" })
# An equivalence miter is UNSAT exactly when the two forms agree on every assignment.
Add-Check -Name 'every-equivalence-miter-unsat' -Ok ($notUnsat.Count -eq 0) `
    -Detail $(if ($notUnsat.Count -eq 0) { "$($satRows.Count) miter(s) UNSAT" }
              else { "miter not UNSAT: $($notUnsat -join ', ')" })

$countRows = @($results.bddDnnf)
$disagree = @($countRows | Where-Object { -not $_.modelCountsAgree } |
    ForEach-Object { [string]$_.name })
Add-Check -Name 'independent-counters-agree' -Ok ($disagree.Count -eq 0) `
    -Detail $(if ($disagree.Count -eq 0) { "BDD and d-DNNF agree on all $($countRows.Count) model count(s)" }
              else { "counts disagree: $($disagree -join ', ')" })

# --- the competitor side actually ran ----------------------------------------------------------
# Every adapter self-skips when its tool is absent, so an all-pending report would otherwise look
# like a successful reproduction. Require evidence that real competitor output was merged.

$competitors = @(
    @{ Name = 'sympy_out.md'; Label = 'SymPy / PyEDA' }
    @{ Name = 'sat_out.md'; Label = 'CaDiCaL / Kissat' }
    @{ Name = 'z3_out.md'; Label = 'Z3' }
    @{ Name = 'mc_out.md'; Label = 'd4 / c2d (#SAT)' }
    @{ Name = 'logicng_out.md'; Label = 'LogicNG BDD' }
)

$populated = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.List[string]]::new()
foreach ($c in $competitors) {
    $path = Join-Path $ComparisonPath $c.Name
    if (-not (Test-Path $path)) { $pending.Add("$($c.Label) (no output file)"); continue }

    $text = Get-Content -Raw $path
    # A self-skipped adapter writes only a note; a real run writes Markdown table rows.
    $rowCount = ([regex]::Matches($text, '(?m)^\s*\|')).Count
    if ($rowCount -ge 3 -and $text -notmatch 'not built in this image') {
        $populated.Add("$($c.Label) ($rowCount table line(s))")
    }
    else {
        $pending.Add($c.Label)
    }
}

Add-Check -Name 'competitor-columns-populated' -Ok ($populated.Count -ge $RequireCompetitors) `
    -Detail ("populated: $($populated.Count)/$($competitors.Count)" +
             $(if ($populated.Count -gt 0) { " [$($populated -join '; ')]" } else { '' }) +
             $(if ($pending.Count -gt 0) { " | pending: $($pending -join ', ')" } else { '' }) +
             " | required at least $RequireCompetitors")

# merged.md is what a reader actually opens; it must carry the merged tables, not just a header.
$mergedRows = ([regex]::Matches($merged, '(?m)^\s*\|')).Count
Add-Check -Name 'merged-report-has-tables' -Ok ($mergedRows -ge 20) `
    -Detail "$mergedRows table line(s) in merged.md"

# --- determinism against a committed run -------------------------------------------------------

if ($CompareWith) {
    if (-not (Test-Path $CompareWith)) {
        Add-Check -Name 'deterministic-vs-committed' -Ok $false -Detail "baseline not found at $CompareWith"
    }
    else {
        # Strip everything the methodology declares machine-dependent, then require an exact match.
        function Remove-TimingFields {
            param($Node)
            if ($null -eq $Node) { return $null }
            if ($Node -is [System.Object[]]) { return @($Node | ForEach-Object { Remove-TimingFields $_ }) }
            if ($Node -is [System.Management.Automation.PSCustomObject]) {
                $clean = [ordered]@{}
                foreach ($property in $Node.PSObject.Properties) {
                    if ($property.Name -match 'Ms(Median)?$' -or
                        $property.Name -in @('allocatedBytes', 'generatedAtUtc', 'conflicts')) { continue }
                    $clean[$property.Name] = Remove-TimingFields $property.Value
                }
                return [pscustomobject]$clean
            }
            return $Node
        }

        $baseline = Get-Content -Raw $CompareWith | ConvertFrom-Json
        $freshJson = (Remove-TimingFields $results) | ConvertTo-Json -Depth 12 -Compress
        $baseJson = (Remove-TimingFields $baseline) | ConvertTo-Json -Depth 12 -Compress
        Add-Check -Name 'deterministic-vs-committed' -Ok ($freshJson -eq $baseJson) `
            -Detail $(if ($freshJson -eq $baseJson) { 'every non-timing field is identical to the committed run' }
                      else { "non-timing fields differ from $CompareWith (sizes: $($freshJson.Length) vs $($baseJson.Length) chars)" })
    }
}

# --- report ------------------------------------------------------------------------------------

$failed = @($checks | Where-Object { $_.status -eq 'fail' })

$report = [ordered]@{
    reportVersion  = 1
    tool           = 'tools/verify_comparison_reproduction.ps1'
    comparisonPath = (Resolve-Path $ComparisonPath).Path
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    environment    = [ordered]@{
        runtime         = [string]$manifest.runtimeIdentifier
        os              = [string]$manifest.osDescription
        architecture    = [string]$manifest.architecture
        processorCount  = $manifest.processorCount
        cpuPolicy       = [string]$manifest.cpuPolicy
        corpusSha256    = [string]$results.corpus.sha256
        functionCount   = $results.corpus.functionCount
    }
    competitors    = [ordered]@{
        populated = @($populated)
        pending   = @($pending)
    }
    limitations    = @(
        'Wall-clock timings are machine-dependent and are never asserted here; only correctness, provenance and coverage are.',
        'A populated competitor column proves the adapter ran, not that its tool version matches the pinned container - use the container for that.',
        'This verifies a reproduction on the machine it ran on. Independent reproduction on a third party''s runner remains open - see the benchmark-result limits in doc/CLAIMS.md.'
    )
    summary        = [ordered]@{
        checks = $checks.Count
        failed = $failed.Count
        result = $(if ($failed.Count -eq 0) { 'pass' } else { 'fail' })
    }
    checks         = @($checks)
}

Write-Utf8NoBom -Path $ReportPath -Content ($report | ConvertTo-Json -Depth 8)

Write-Host ''
Write-Host '==== Comparison reproduction summary ===='
Write-Host ("  checks: {0}" -f $report.summary.checks)
Write-Host ("  failed: {0}" -f $report.summary.failed)
Write-Host ("  report: {0}" -f $ReportPath)
Write-Host '========================================'

if ($failed.Count -gt 0) {
    Write-Error ("Comparison reproduction failed {0} check(s)." -f $failed.Count)
    exit 1
}

Write-Host 'Comparison reproduction verified.'
exit 0
