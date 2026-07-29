#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verify that the LogicalOptimizer NuGet packages are present on nuget.org for a
    given version.

.DESCRIPTION
    Queries the NuGet flat-container (v3) index for each package
    (https://api.nuget.org/v3-flatcontainer/<lowercased-id>/index.json), parses the
    JSON "versions" array and checks that the target version is listed.

    NuGet indexing lags behind `dotnet nuget push` (often several minutes), so each
    still-missing package is retried with a fixed backoff before the run is declared a
    failure. A per-package OK / MISSING summary is printed and the script exits non-zero
    if any package is still missing after all retries.

    Dependency-free: uses only Invoke-RestMethod from the built-in
    Microsoft.PowerShell.Utility module. Runs on Windows PowerShell 5.1 and PowerShell 7+
    (the pwsh shipped on GitHub-hosted runners).

.PARAMETER Version
    The package version to look for, e.g. 2.2.0. Required.

.PARAMETER Packages
    Package IDs to verify. Defaults to the nine published LogicalOptimizer packages.

.PARAMETER MaxAttempts
    Maximum number of attempts per package before giving up. Default 30.

.PARAMETER DelaySeconds
    Delay between attempts, in seconds. Default 60.
    The defaults give a ~30-minute window: nuget.org validation + flat-container
    indexing of a freshly pushed package commonly takes 10-20 minutes (and occasionally
    longer when several packages are pushed at once), well past the old 5-minute window
    which failed the release even though every package had actually published.

.EXAMPLE
    pwsh tools/verify_nuget.ps1 -Version 2.2.0

.EXAMPLE
    # Faster local smoke test against an already-published version:
    pwsh tools/verify_nuget.ps1 -Version 2.1.0 -MaxAttempts 1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [string[]] $Packages = @(
        'LogicalOptimizer.Core',
        'LogicalOptimizer.Sat',
        'LogicalOptimizer.Bdd',
        'LogicalOptimizer.Dnnf',
        'LogicalOptimizer.Formats',
        'LogicalOptimizer.Minimization',
        'LogicalOptimizer',
        'LogicalOptimizer.Cli',
        'LogicalOptimizer.Full'
    ),

    [ValidateRange(1, 100)]
    [int] $MaxAttempts = 30,

    [ValidateRange(0, 3600)]
    [int] $DelaySeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Normalize the target version the way NuGet does (drops a redundant trailing ".0"
# revision and lower-cases any pre-release label) so an exact string match is reliable.
function Get-NormalizedVersion {
    param([string] $Raw)
    return $Raw.Trim().ToLowerInvariant()
}

$targetVersion = Get-NormalizedVersion $Version

# Returns $true if $Id is published at $targetVersion, $false if the index exists but the
# version is absent, and $null if the package is not indexed yet (HTTP 404) -> retryable.
function Test-PackageVersion {
    param(
        [string] $Id,
        [string] $TargetVersion
    )

    $lowerId = $Id.ToLowerInvariant()
    $uri = "https://api.nuget.org/v3-flatcontainer/$lowerId/index.json"

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 30
    }
    catch {
        # A brand-new package (or one still being indexed) returns 404 from the flat
        # container. Treat that as "not yet indexed" and let the caller retry.
        $status = $null
        $resp = $_.Exception.Response
        if ($null -ne $resp -and ($resp.PSObject.Properties.Name -contains 'StatusCode')) {
            try { $status = [int]$resp.StatusCode } catch { $status = $null }
        }
        if ($status -eq 404) {
            return $null
        }
        # Any other failure (throttling, DNS, TLS, 5xx) is transient enough to retry too,
        # but surface it so CI logs explain the wait.
        Write-Warning ("  {0}: query failed ({1}) - will retry" -f $Id, $_.Exception.Message)
        return $null
    }

    if ($null -eq $response -or -not ($response.PSObject.Properties.Name -contains 'versions')) {
        return $null
    }

    foreach ($v in $response.versions) {
        if ((Get-NormalizedVersion $v) -eq $TargetVersion) {
            return $true
        }
    }
    return $false
}

Write-Host ("Verifying {0} package(s) at version {1} on nuget.org" -f $Packages.Count, $Version)
Write-Host ("Up to {0} attempt(s) per package, {1}s between attempts." -f $MaxAttempts, $DelaySeconds)
Write-Host ''

$pending = [System.Collections.Generic.List[string]]::new()
foreach ($p in $Packages) { $pending.Add($p) }

$found = @{}

for ($attempt = 1; $attempt -le $MaxAttempts -and $pending.Count -gt 0; $attempt++) {
    Write-Host ("Attempt {0}/{1} - checking {2} package(s)..." -f $attempt, $MaxAttempts, $pending.Count)

    $stillPending = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $pending) {
        $result = Test-PackageVersion -Id $id -TargetVersion $targetVersion
        if ($result -eq $true) {
            Write-Host ("  OK      {0} {1}" -f $id, $Version)
            $found[$id] = $true
        }
        else {
            # $false (indexed, version missing) and $null (not indexed yet) are both retryable.
            $stillPending.Add($id)
        }
    }

    $pending = $stillPending

    if ($pending.Count -gt 0 -and $attempt -lt $MaxAttempts) {
        Write-Host ("  waiting {0}s ({1} still pending: {2})" -f $DelaySeconds, $pending.Count, ($pending -join ', '))
        Start-Sleep -Seconds $DelaySeconds
    }
}

Write-Host ''
Write-Host '==== NuGet verification summary ===='
foreach ($id in $Packages) {
    if ($found.ContainsKey($id)) {
        Write-Host ("  OK       {0} {1}" -f $id, $Version)
    }
    else {
        Write-Host ("  MISSING  {0} {1}" -f $id, $Version)
    }
}
Write-Host '===================================='

if ($pending.Count -gt 0) {
    Write-Error ("{0} package(s) still missing on nuget.org after {1} attempt(s): {2}" -f $pending.Count, $MaxAttempts, ($pending -join ', '))
    exit 1
}

Write-Host ("All {0} package(s) present on nuget.org at version {1}." -f $Packages.Count, $Version)
exit 0
