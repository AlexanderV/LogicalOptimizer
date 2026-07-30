#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Assemble one release evidence bundle: every verification artifact for a release in a single
    directory with an index, so an outside reader can check the release's claims without hunting
    through workflow logs.

.DESCRIPTION
    A release already produces the evidence - a package contract audit, a nuget.org index check, an
    installation and Native AOT smoke test from the published packages, a test run, checksums, and
    a build provenance attestation - but each piece lives somewhere different and expires with the
    workflow logs. This script collects them into one bundle:

        evidence/
          INDEX.md                     what each file proves, and what it does NOT prove
          manifest.json                machine-readable index + SHA-256 of every bundled file
          package-contract-report.json contents audit of every .nupkg  (verify_package_contract.ps1)
          nuget-index-report.json      packages present on nuget.org   (verify_nuget.ps1)
          aot-package-smoke.json       native binary built from the PUBLISHED package
          test-summary.json            per-push suite counts parsed from the .trx
          exhaustive-evidence.json     the claim-critical exhaustive sweeps, re-run for this commit
          SHA256SUMS.txt               checksums of the published .nupkg/.snupkg
          claim-changes.md             the CHANGELOG section for this version
          verifying-provenance.md      how to verify the signed build attestation yourself

    Missing inputs are recorded in the manifest as 'absent' rather than silently skipped, and
    -RequireAll turns any absent required input into a failure - so a bundle can never look
    complete when it is not.

.PARAMETER Version
    The release version, e.g. 3.1.0. Required.

.PARAMETER OutputPath
    Directory to build the bundle in. Default 'evidence'. Created if absent.

.PARAMETER PackageContractReport
    JSON report from tools/verify_package_contract.ps1.

.PARAMETER NuGetIndexReport
    JSON report from tools/verify_nuget.ps1 -ReportPath.

.PARAMETER AotReport
    JSON report from tools/smoke_install.ps1 -IncludeAot -AotReportPath.

.PARAMETER TrxPath
    A .trx test result file; test counts are parsed from it into test-summary.json.

.PARAMETER ExhaustiveTrxPath
    The .trx from the claim-critical run (`--filter "Category=ReleaseEvidence"`), parsed into
    exhaustive-evidence.json. Kept separate from the general summary on purpose: this is the
    evidence README and doc/CLAIMS.md cite, so it must be visibly present or visibly absent.

.PARAMETER ChecksumsPath
    SHA256SUMS.txt covering the published .nupkg/.snupkg files.

.PARAMETER BenchmarkManifest
    Optional benchmark manifest/raw results to include (file or directory).

.PARAMETER ChangelogPath
    CHANGELOG.md to extract this version's section from. Default 'CHANGELOG.md'.

.PARAMETER Repository
    owner/repo used in the provenance verification instructions.

.PARAMETER RequireAll
    Fail if any of the four core inputs (package contract, nuget index, AOT, checksums) is absent.
    Intended for the release workflow; leave off for a local dry run.

.EXAMPLE
    pwsh tools/build_evidence_bundle.ps1 -Version 3.1.0 `
        -PackageContractReport package-contract-report.json `
        -NuGetIndexReport nuget-index-report.json `
        -AotReport aot-package-smoke.json `
        -TrxPath TestResults/release.trx `
        -ChecksumsPath artifacts/SHA256SUMS.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [string] $OutputPath = 'evidence',

    [string] $PackageContractReport,
    [string] $NuGetIndexReport,
    [string] $AotReport,
    [string] $TrxPath,
    [string] $ExhaustiveTrxPath,
    [string] $ChecksumsPath,
    [string] $BenchmarkManifest,

    [string] $ChangelogPath = 'CHANGELOG.md',

    [string] $Repository = 'AlexanderV/LogicalOptimizer',

    [switch] $RequireAll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1's `Out-File -Encoding utf8` writes a BOM, which trips strict JSON
# parsers; pwsh 7 does not. Write BOM-free either way so a bundle is identical on both.
function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Content
    )
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

$bundle = if (Test-Path $OutputPath) { (Resolve-Path $OutputPath).Path } else { $OutputPath }
New-Item -ItemType Directory -Path $bundle -Force | Out-Null

Write-Host ("Building release evidence bundle for {0}" -f $Version)
Write-Host ("Output: {0}" -f $bundle)
Write-Host ''

$items = [System.Collections.Generic.List[object]]::new()

function Add-BundleItem {
    param(
        # Deliberately allowed to be empty/absent: an input that was not produced is recorded as
        # 'absent' in the manifest rather than dropped, so the bundle cannot look complete.
        [AllowEmptyString()] [AllowNull()] [string] $SourcePath,
        [Parameter(Mandatory = $true)] [string] $TargetName,
        [Parameter(Mandatory = $true)] [string] $Proves,
        [switch] $Required
    )

    if ([string]::IsNullOrWhiteSpace($SourcePath) -or -not (Test-Path $SourcePath)) {
        Write-Host ("   absent  {0}" -f $TargetName)
        $items.Add([ordered]@{
            file     = $TargetName
            status   = 'absent'
            proves   = $Proves
            required = [bool]$Required
            source   = $SourcePath
        })
        return
    }

    $destination = Join-Path $bundle $TargetName
    Copy-Item -Path $SourcePath -Destination $destination -Recurse -Force
    Write-Host ("   ok      {0}" -f $TargetName)
    $items.Add([ordered]@{
        file     = $TargetName
        status   = 'present'
        proves   = $Proves
        required = [bool]$Required
        source   = (Resolve-Path $SourcePath).Path
    })
}

# --- collected verification reports ------------------------------------------------------------

Add-BundleItem -SourcePath $PackageContractReport -TargetName 'package-contract-report.json' -Required:$RequireAll `
    -Proves 'Every published .nupkg was opened and audited: package-specific README present, distinct substantial description, tags, project/repository URLs, Apache-2.0 SPDX expression, symbols .snupkg with a .pdb, contracted target frameworks, and no third-party runtime dependency.'

Add-BundleItem -SourcePath $NuGetIndexReport -TargetName 'nuget-index-report.json' -Required:$RequireAll `
    -Proves 'All nine packages are present in the nuget.org flat-container index at this version.'

Add-BundleItem -SourcePath $AotReport -TargetName 'aot-package-smoke.json' -Required:$RequireAll `
    -Proves 'A Native AOT binary was compiled against the PUBLISHED package (not an in-repo project reference) and produced the expected optimized expression, equivalence proof and MinimalProven status.'

Add-BundleItem -SourcePath $ChecksumsPath -TargetName 'SHA256SUMS.txt' -Required:$RequireAll `
    -Proves 'SHA-256 of exactly the .nupkg/.snupkg bytes that were pushed, so a downloaded package can be compared byte-for-byte.'

# Recorded unconditionally: skipping the item when the caller passes nothing would let a bundle
# omit the benchmark provenance silently, which is exactly the "looks complete but is not" failure
# the manifest exists to prevent. An empty -BenchmarkManifest lands as 'absent'.
Add-BundleItem -SourcePath $BenchmarkManifest -TargetName 'benchmarks' `
    -Proves 'Benchmark and comparison provenance: which version, corpus (with its SHA-256), hardware and runtime the published numbers came from, and the equivalence verdict for every row.'

# --- test summaries parsed from the .trx files -------------------------------------------------

function Add-TrxSummary {
    param(
        [AllowEmptyString()] [AllowNull()] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $TargetName,
        [Parameter(Mandatory = $true)] [string] $Scope,
        [Parameter(Mandatory = $true)] [string] $Proves,
        [switch] $Required
    )

    if (-not $Path -or -not (Test-Path $Path)) {
        Write-Host ("   absent  {0}" -f $TargetName)
        $items.Add([ordered]@{
            file = $TargetName; status = 'absent'; proves = $Proves
            required = [bool]$Required; source = $Path
        })
        return
    }

    $trx = [xml](Get-Content -Raw $Path)
    $counters = $trx.DocumentElement.SelectSingleNode("//*[local-name()='Counters']")
    $summary = [ordered]@{
        reportVersion = 1
        source        = Split-Path -Leaf $Path
        # Exactly which tests these counts cover. Without it a reader cannot tell a full run from
        # a filtered one, and "1239 passed" would imply more than it proves.
        scope         = $Scope
        total         = [int]$counters.total
        executed      = [int]$counters.executed
        passed        = [int]$counters.passed
        failed        = [int]$counters.failed
        result        = $(if ([int]$counters.failed -eq 0) { 'pass' } else { 'fail' })
    }

    Write-Utf8NoBom -Path (Join-Path $bundle $TargetName) -Content ($summary | ConvertTo-Json -Depth 4)
    Write-Host ("   ok      {0} ({1} passed, {2} failed)" -f $TargetName, $summary.passed, $summary.failed)
    $items.Add([ordered]@{
        file     = $TargetName
        status   = 'present'
        proves   = "$Proves Counts for this release build: $($summary.passed) passed, $($summary.failed) failed."
        required = [bool]$Required
        source   = (Resolve-Path $Path).Path
    })
}

Add-TrxSummary -Path $TrxPath -TargetName 'test-summary.json' `
    -Scope 'dotnet test --filter "Category!=Performance&Category!=Exhaustive"' `
    -Proves 'The per-push suite passed for the exact commit being published. Timing-sensitive (Performance) and exhaustive-sweep (Exhaustive) tests are excluded here and reported separately.'

# Separate on purpose: this is the evidence README and doc/CLAIMS.md point at for the "verified"
# and "MinimalProven" claims. Folding it into the general count would hide whether it ran at all.
Add-TrxSummary -Path $ExhaustiveTrxPath -TargetName 'exhaustive-evidence.json' -Required:$RequireAll `
    -Scope 'dotnet test --filter "Category=ReleaseEvidence"' `
    -Proves 'The claim-critical exhaustive sweeps re-ran for this commit: all 65534 non-constant 4-variable functions preserve semantics (the "verified" claim) and all of them report MinimalProven (the "minimal" claim).'

# --- claim changes vs the previous release -----------------------------------------------------

if (Test-Path $ChangelogPath) {
    $lines = Get-Content $ChangelogPath
    $section = [System.Collections.Generic.List[string]]::new()
    $inSection = $false
    foreach ($line in $lines) {
        if ($line -match '^##\s') {
            if ($inSection) { break }
            if ($line -match ("^##\s+\[" + [regex]::Escape($Version) + "\]")) { $inSection = $true }
        }
        if ($inSection) { $section.Add($line) }
    }

    $body = if ($section.Count -gt 0) {
        ($section -join "`n").TrimEnd()
    } else {
        "No `## [$Version]` section found in $ChangelogPath at bundle time."
    }

    Write-Utf8NoBom -Path (Join-Path $bundle 'claim-changes.md') -Content @"
# Claim changes in $Version

What changed in what this release claims, taken verbatim from
[CHANGELOG.md](https://github.com/$Repository/blob/v$Version/CHANGELOG.md).
Read it together with ``package-contract-report.json``: the changelog says what was promised, the
report says what shipped.

$body
"@

    Write-Host ("   ok      claim-changes.md ({0} line(s) from the changelog)" -f $section.Count)
    $items.Add([ordered]@{
        file     = 'claim-changes.md'
        status   = 'present'
        proves   = 'What this release claims to change relative to the previous one, verbatim from the changelog.'
        required = $false
        source   = (Resolve-Path $ChangelogPath).Path
    })
}

# --- provenance instructions -------------------------------------------------------------------

Write-Utf8NoBom -Path (Join-Path $bundle 'verifying-provenance.md') -Content @"
# Verifying this release yourself

Nothing here asks you to trust the bundle. Every step below can be run against the packages as
downloaded from nuget.org.

## 1. The packages were built by this repository's release workflow

The release workflow signs a build provenance attestation for exactly the ``.nupkg`` bytes it
pushed. Download a package and verify it with the GitHub CLI:

``````bash
curl -sSLO https://api.nuget.org/v3-flatcontainer/logicaloptimizer/$Version/logicaloptimizer.$Version.nupkg
gh attestation verify logicaloptimizer.$Version.nupkg --repo $Repository
``````

A successful verification names the workflow (``release.yml``) and the commit the package was
built from.

## 2. The bytes match what was published

``SHA256SUMS.txt`` in this bundle lists the SHA-256 of every ``.nupkg`` and ``.snupkg`` at push
time:

``````bash
sha256sum -c SHA256SUMS.txt        # in a directory holding the downloaded packages
``````

## 3. The package contents satisfy the contract

Re-run the same audit that produced ``package-contract-report.json``, against the packages you
downloaded rather than the ones the workflow built:

``````bash
pwsh tools/verify_package_contract.ps1 -ArtifactsPath downloaded -Version $Version
``````

## 4. The packages install, run, and compile with Native AOT

``````bash
pwsh tools/smoke_install.ps1 -Version $Version -IncludeAot
``````

This creates a throwaway project outside the repository, installs the published package, asserts
the optimization result together with its equivalence proof and minimality status, installs the
CLI tool, and then compiles and runs the same program as a Native AOT binary.

## 5. The CLI JSON report matches its published schema

``````bash
logical-optimizer --format=json "a & b | a & c" > report.json
check-jsonschema --schemafile schema/cli-report-v1.schema.json report.json
``````

## 6. The comparison numbers reproduce

``benchmarks/`` in this bundle carries the corpus checksum, the environment the numbers came from,
and every row's equivalence verdict. Reproduce them on your machine with the sequence from
``doc/COMPARISON_METHODOLOGY.md`` — and then **check** the run rather than trusting its exit code:

``````bash
docker build -t logicopt-p0p2 tools/comparison
docker run --rm -v "`$PWD:/work" logicopt-p0p2
pwsh tools/verify_comparison_reproduction.ps1 -RequireCompetitors 3
``````

The verifier fails an all-``pending`` report, checks the corpus by SHA-256 rather than by name, and
asserts correctness independently of timing. **If you run this, please say so** — an independent
reproduction is the one piece of evidence this project cannot produce for itself (see the
``benchmark result`` limits in ``doc/CLAIMS.md``).

## What this bundle does not prove

- It says nothing about performance relative to other libraries. That lives in the separately
  reproducible comparison (``doc/COMPARISON_METHODOLOGY.md``), pinned to its own corpus, hardware
  and competitor versions.
- ``package-contract-report.json`` audits package *contents*; SourceLink is checked only to the
  extent that repository url/commit metadata and a ``.pdb`` are present - stepping into sources
  from a debugger is not exercised.
- The test summary covers this release build's suite excluding the Performance and Exhaustive
  categories; it is not a coverage or correctness proof for every input.
"@

Write-Host '   ok      verifying-provenance.md'
$items.Add([ordered]@{
    file     = 'verifying-provenance.md'
    status   = 'present'
    proves   = 'Reproduction instructions: how to verify the attestation, checksums, package contract, install/AOT smoke and CLI schema independently.'
    required = $false
    source   = 'generated'
})

# --- manifest + index --------------------------------------------------------------------------

foreach ($item in $items) {
    $path = Join-Path $bundle $item.file
    if ($item.status -eq 'present' -and (Test-Path $path) -and -not (Test-Path $path -PathType Container)) {
        $item['sha256'] = (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $item['bytes'] = (Get-Item $path).Length
    }
}

$missingRequired = @($items | Where-Object { $_.required -and $_.status -eq 'absent' })

$manifest = [ordered]@{
    manifestVersion = 1
    tool            = 'tools/build_evidence_bundle.ps1'
    version         = $Version
    repository      = $Repository
    generatedAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
    summary         = [ordered]@{
        items           = $items.Count
        present         = @($items | Where-Object { $_.status -eq 'present' }).Count
        absent          = @($items | Where-Object { $_.status -eq 'absent' }).Count
        missingRequired = $missingRequired.Count
        result          = $(if ($missingRequired.Count -eq 0) { 'complete' } else { 'incomplete' })
    }
    items           = @($items)
}
Write-Utf8NoBom -Path (Join-Path $bundle 'manifest.json') -Content ($manifest | ConvertTo-Json -Depth 6)

$rows = ($items | ForEach-Object {
    $mark = if ($_.status -eq 'present') { 'included' } else { '**absent**' }
    "| ``$($_.file)`` | $mark | $($_.proves) |"
}) -join "`n"

Write-Utf8NoBom -Path (Join-Path $bundle 'INDEX.md') -Content @"
# Release evidence bundle - LogicalOptimizer $Version

Everything needed to check this release's claims, in one place. Each file states what it proves;
``verifying-provenance.md`` shows how to reproduce every check yourself against packages
downloaded from nuget.org, without trusting this bundle.

Bundle status: **$($manifest.summary.result)** ($($manifest.summary.present) of $($items.Count) items present).

| File | Status | What it proves |
|---|---|---|
$rows

``manifest.json`` carries the same table machine-readably, plus the SHA-256 and size of every file
in the bundle.

## Reading order

1. ``claim-changes.md`` - what this release claims that the previous one did not.
2. ``package-contract-report.json`` - whether the packages actually ship what is claimed.
3. ``nuget-index-report.json`` and ``SHA256SUMS.txt`` - that those exact bytes were published.
4. ``aot-package-smoke.json`` and ``test-summary.json`` - that the published artifacts work.
5. ``verifying-provenance.md`` - how to redo all of it independently.

Definitions of *verified*, *minimal*, *dependency-free* and *Native AOT support*, each linked to
the test or CI check that backs it, are in
[SUPPORT.md](https://github.com/$Repository/blob/v$Version/SUPPORT.md) and
[README.md](https://github.com/$Repository/blob/v$Version/README.md).
"@

Write-Host ''
Write-Host '==== Evidence bundle summary ===='
Write-Host ("  items:   {0}" -f $manifest.summary.items)
Write-Host ("  present: {0}" -f $manifest.summary.present)
Write-Host ("  absent:  {0}" -f $manifest.summary.absent)
Write-Host ("  bundle:  {0}" -f $bundle)
Write-Host '================================'

if ($missingRequired.Count -gt 0) {
    Write-Host ''
    foreach ($m in $missingRequired) { Write-Host ("  MISSING {0} (expected at '{1}')" -f $m.file, $m.source) }
    Write-Error ("Evidence bundle incomplete: {0} required item(s) missing." -f $missingRequired.Count)
    exit 1
}

Write-Host ("Evidence bundle complete for {0}." -f $Version)
exit 0
