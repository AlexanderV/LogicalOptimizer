#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reproducible installation smoke test: install the PUBLISHED packages from nuget.org into a
    throwaway project and prove they actually work for a consumer.

.DESCRIPTION
    Verifying that a package is present in the nuget.org index (tools/verify_nuget.ps1) does not
    prove it is usable — the assembly could be missing from lib/, a dependency could be
    unresolvable, or the tool could fail to launch. This script exercises the real consumer path:

      1. creates a temporary console project OUTSIDE the repository (so the repo's
         Directory.Build.props and project references cannot influence the result);
      2. installs the requested package version explicitly from nuget.org;
      3. runs a program that optimizes an expression through the public API and asserts the
         result, the equivalence proof and the minimality status;
      4. installs the CLI as a global tool and asserts its --format=json report.

    Exits non-zero on the first failure. Safe to run locally against any published version.

.PARAMETER Version
    The published package version to install, e.g. 3.1.0. Required.

.PARAMETER SkipTool
    Skip the global-tool part (useful where the tool path is not on PATH for the session).

.EXAMPLE
    pwsh tools/smoke_install.ps1 -Version 3.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [switch] $SkipTool
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$nugetSource = 'https://api.nuget.org/v3/index.json'
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("lo-smoke-" + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))

function Invoke-Step {
    param(
        [string] $Name,
        [scriptblock] $Action
    )
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

Write-Host ("Smoke-testing published LogicalOptimizer {0} from nuget.org" -f $Version)
Write-Host ("Work directory: {0}" -f $workDir)
Write-Host ''

try {
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    Push-Location $workDir
    try {
        Invoke-Step 'dotnet new console' { dotnet new console -o Probe --no-restore }

        $probe = Join-Path $workDir 'Probe'

        # A consumer program: optimize, verify equivalence, read the explicit minimality status.
        @'
using LogicalOptimizer;

var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");

Console.WriteLine($"optimized={result.Optimized}");
Console.WriteLine($"equivalent={result.IsEquivalent()}");
Console.WriteLine($"minimality={result.MinimizationStatus}");

if (result.Optimized != "a & (b | c)") { Console.Error.WriteLine("FAIL: unexpected optimized form"); return 1; }
if (!result.IsEquivalent()) { Console.Error.WriteLine("FAIL: result not verified equivalent"); return 1; }
if (result.MinimizationStatus != MinimizationStatus.MinimalProven) { Console.Error.WriteLine("FAIL: minimality not proven"); return 1; }

Console.WriteLine("OK");
return 0;
'@ | Set-Content -Path (Join-Path $probe 'Program.cs') -Encoding utf8

        Invoke-Step "dotnet add package LogicalOptimizer $Version" {
            dotnet add (Join-Path $probe 'Probe.csproj') package LogicalOptimizer --version $Version --source $nugetSource
        }

        Write-Host '==> dotnet run (library smoke test)'
        $output = dotnet run --project (Join-Path $probe 'Probe.csproj') 2>&1
        $runExit = $LASTEXITCODE
        $output | ForEach-Object { Write-Host "    $_" }
        if ($runExit -ne 0 -or ($output -join "`n") -notmatch 'OK') {
            throw "Library smoke test failed (exit $runExit)"
        }

        if (-not $SkipTool) {
            Invoke-Step "dotnet tool install LogicalOptimizer.Cli $Version" {
                dotnet tool install --global LogicalOptimizer.Cli --version $Version --add-source $nugetSource
            }

            Write-Host '==> logical-optimizer --format=json (CLI smoke test)'
            $json = logical-optimizer --format=json 'a & b | a & c' 2>&1
            $cliExit = $LASTEXITCODE
            $json | ForEach-Object { Write-Host "    $_" }
            $joined = $json -join "`n"
            if ($cliExit -ne 0) { throw "CLI returned exit code $cliExit" }
            foreach ($needle in @('"schemaVersion"', '"equivalent": true', 'MinimalProven')) {
                if ($joined -notmatch [regex]::Escape($needle)) {
                    throw "CLI JSON report is missing $needle"
                }
            }
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    Write-Host ("Installation smoke test PASSED for {0}." -f $Version)
    exit 0
}
catch {
    Write-Host ''
    Write-Error ("Installation smoke test FAILED: {0}" -f $_.Exception.Message)
    exit 1
}
finally {
    if (Test-Path $workDir) {
        try { Remove-Item -Recurse -Force $workDir -ErrorAction Stop } catch { }
    }
}
