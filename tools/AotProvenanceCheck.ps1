# Shared AOT-provenance cross-check, dot-sourced by tools/build_evidence_bundle.ps1 and
# exercised directly by AotProvenanceContractTests (both source modes, deterministic, offline).
#
# The contract (F-14): an AOT smoke report must be tieable to the bytes it actually ran
# against. In the pre-publish release gate that means its consolidatedPackageSha256 MUST
# match the SHA256SUMS.txt entry for the consolidated package — the same bytes that get
# pushed. Against the published nuget.org copy the field MUST be null, because nuget.org
# repository-signs packages and their bytes legitimately differ.

function Test-AotProvenance {
    <#
    .SYNOPSIS
        Validate that an AOT smoke report's provenance is internally consistent and, for a
        pre-publish report, matches the checksum manifest. Returns $null when consistent,
        otherwise a human-readable failure message.
    #>
    param(
        [Parameter(Mandatory = $true)] [string] $AotReportPath,
        [string] $ChecksumsPath,
        [Parameter(Mandatory = $true)] [string] $Version
    )

    $report = Get-Content -Raw -Path $AotReportPath | ConvertFrom-Json
    $source = [string]$report.source
    $sha = $null
    if (($report.PSObject.Properties.Name -contains 'consolidatedPackageSha256') -and
        $report.consolidatedPackageSha256) {
        $sha = ([string]$report.consolidatedPackageSha256).ToLowerInvariant()
    }

    if ($source -like 'local packed artifacts*') {
        if (-not $sha) {
            return 'AOT report claims a local pre-publish source but carries no ' +
                   'consolidatedPackageSha256 - the evidence cannot be tied to the pushed bytes.'
        }
        if (-not $ChecksumsPath -or -not (Test-Path $ChecksumsPath)) {
            return 'AOT report is pre-publish but no SHA256SUMS.txt was provided to check it against.'
        }
        $expectedName = "LogicalOptimizer.$Version.nupkg"
        $line = Get-Content -Path $ChecksumsPath |
            Where-Object { $_ -match [regex]::Escape($expectedName) } | Select-Object -First 1
        if (-not $line) {
            return "SHA256SUMS.txt has no entry for $expectedName."
        }
        $checksum = ($line.Trim() -split '\s+')[0].ToLowerInvariant()
        if ($checksum -ne $sha) {
            return "AOT report consolidatedPackageSha256 ($sha) does not match SHA256SUMS.txt " +
                   "($checksum) for $expectedName - the smoke ran against different bytes than were pushed."
        }
        return $null
    }

    if ($source -eq 'published nuget.org package') {
        if ($sha) {
            return 'AOT report claims the published nuget.org package but carries a local ' +
                   'consolidatedPackageSha256 - the provenance is inconsistent.'
        }
        return $null
    }

    return "AOT report has an unrecognized source '$source' - cannot validate provenance."
}
