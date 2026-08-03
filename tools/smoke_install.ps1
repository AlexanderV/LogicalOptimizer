#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reproducible installation smoke test: install the packed packages into a throwaway project
    and prove they actually work for a consumer. Runs against nuget.org by default, or against
    a local folder of freshly packed artifacts via -Source (the pre-publish release gate).

.DESCRIPTION
    Verifying that a package is present in the nuget.org index (tools/verify_nuget.ps1) does not
    prove it is usable — the assembly could be missing from lib/, a dependency could be
    unresolvable, or the tool could fail to launch. This script exercises the real consumer path:

      1. creates a temporary console project OUTSIDE the repository (so the repo's
         Directory.Build.props and project references cannot influence the result);
      2. installs the requested package version explicitly from the requested source
         (nuget.org, or the local pre-publish artifacts);
      3. runs a program that optimizes an expression through the public API and asserts the
         result, the equivalence proof and the minimality status;
      4. installs the CLI as a global tool and asserts its --format=json report;
      5. installs EVERY forwarding-shell ID into its own separate project and asserts that the
         full assembly set loads with public types on it (the v4.0 forwarding contract);
      6. optionally (-IncludeAot) publishes that consumer project with PublishAot=true and runs
         the resulting NATIVE binary, so Native AOT support is proven from the PACKAGE bytes
         under test — local pre-publish artifacts or the published nuget.org package — and not
         only from the in-repo project reference that .github/workflows/aot.yml covers.

    Exits non-zero on the first failure. Safe to run locally against any published version, or
    pre-publish against a folder of packed artifacts.

.PARAMETER Version
    The package version to install, e.g. 4.0.0. Required.

.PARAMETER SkipTool
    Skip the global-tool part (useful where the tool path is not on PATH for the session).

.PARAMETER IncludeAot
    Also run the Native AOT publish + native-binary smoke test. Requires a working native
    toolchain (clang + zlib development headers on Linux, MSVC C++ build tools on Windows).

.PARAMETER AotReportPath
    Write a small JSON result for the AOT step here, for inclusion in a release evidence bundle.

.PARAMETER Source
    NuGet source to install from. Defaults to nuget.org (the post-publish scenario). Point it at a
    local folder of packed .nupkg files to run the SAME consumer-path proof BEFORE anything is
    pushed — the pre-publish gate the release workflow runs (OE-05: every check that can refuse a
    release must run before the irreversible push).

.EXAMPLE
    pwsh tools/smoke_install.ps1 -Version 3.1.0

.EXAMPLE
    pwsh tools/smoke_install.ps1 -Version 3.1.0 -IncludeAot -AotReportPath aot-package-smoke.json

.EXAMPLE
    # Pre-publish: prove the freshly packed artifacts install and run, before any push.
    pwsh tools/smoke_install.ps1 -Version 4.0.0 -Source artifacts -IncludeAot
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [switch] $SkipTool,

    [switch] $IncludeAot,

    [string] $AotReportPath,

    [string] $Source = 'https://api.nuget.org/v3/index.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# A local folder source must survive the Push-Location into the throwaway work directory.
$nugetSource = if (Test-Path $Source) { (Resolve-Path $Source).Path } else { $Source }

# Set when this run globally installs the CLI tool, so the finally block can undo it.
$script:cliToolInstalled = $false

# Resolved against the CURRENT directory before we Push-Location into the throwaway work
# directory, which is deleted on exit - a relative report path would vanish with it.
if ($AotReportPath -and -not [System.IO.Path]::IsPathRooted($AotReportPath)) {
    $AotReportPath = Join-Path (Get-Location).Path $AotReportPath
}

$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
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

# The facade is only one of nine packages. A modular package could be broken on its own - an empty
# lib/, a dependency that does not resolve standalone - and still be invisible to a facade-only
# test, because the facade pulls its own copies. So install each package into its OWN project and
# assert the assemblies it is supposed to bring really load with public types on them.
#
# Assembly.Load by simple name deliberately avoids naming any API: the check stays valid as the
# public surface evolves, and it still proves the assembly shipped in the package, targets a
# framework the consumer can use, and is loadable at runtime.
function Test-ModularPackage {
    param(
        [Parameter(Mandatory = $true)] [string] $PackageId,
        # Assemblies expected to be reachable from a reference to $PackageId alone. For the
        # meta-package this is the whole bundle, which is exactly its promise.
        [Parameter(Mandatory = $true)] [string[]] $ExpectedAssemblies,
        [Parameter(Mandatory = $true)] [string] $Root
    )

    $name = 'Probe_' + ($PackageId -replace '\.', '_')
    $projectDirectory = Join-Path $Root $name
    $project = Join-Path $projectDirectory "$name.csproj"

    Invoke-Step "dotnet new console ($PackageId)" {
        dotnet new console -o $projectDirectory --no-restore
    }

    $assemblyList = ($ExpectedAssemblies | ForEach-Object { '"' + $_ + '"' }) -join ', '
    $program = @'
using System.Reflection;

string[] expected = { __ASSEMBLIES__ };
var failed = false;

foreach (var name in expected)
{
    try
    {
        var assembly = Assembly.Load(name);
        var types = assembly.GetExportedTypes().Length;
        Console.WriteLine($"  {name}: {types} public type(s)");
        if (types == 0) { Console.Error.WriteLine($"FAIL: {name} exports no public type"); failed = true; }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL: cannot load {name}: {ex.Message}");
        failed = true;
    }
}

if (failed) { return 1; }
Console.WriteLine("OK");
return 0;
'@.Replace('__ASSEMBLIES__', $assemblyList)

    Set-Content -Path (Join-Path $projectDirectory 'Program.cs') -Value $program -Encoding utf8

    Invoke-Step "dotnet add package $PackageId $Version" {
        dotnet add $project package $PackageId --version $Version --source $nugetSource
    }

    Write-Host "==> dotnet run ($PackageId)"
    $output = dotnet run --project $project 2>&1
    $exit = $LASTEXITCODE
    $output | ForEach-Object { Write-Host "    $_" }
    if ($exit -ne 0 -or ($output -join "`n") -notmatch 'OK') {
        throw "Modular package smoke test failed for $PackageId (exit $exit)"
    }
}

Write-Host ("Smoke-testing LogicalOptimizer {0} from {1}" -f $Version, $nugetSource)
Write-Host ("Work directory: {0}" -f $workDir)
Write-Host ''

try {
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null

    # The probe projects restore during `dotnet run`, not only at `dotnet add package` time, so
    # the source must be visible to restore as well: a NuGet.config at the work-directory root
    # governs every probe underneath it. nuget.org stays listed for the SDK's own needs.
    $sourcesXml = "    <add key=`"smoke-source`" value=`"$nugetSource`" />"
    if ($nugetSource -ne 'https://api.nuget.org/v3/index.json') {
        $sourcesXml += "`n    <add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`" />"
    }
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
$sourcesXml
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $workDir 'NuGet.config') -Encoding utf8

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
            # Retry, because "published" and "installable as a tool" are not the same instant.
            # verify_nuget.ps1 polls the flat-container index and returns as soon as the .nupkg is
            # fetchable there, but `dotnet tool install` needs the package indexed further, and that
            # lags behind by anything from seconds to minutes. Running the two back to back means
            # the tool install can legitimately lose the race and report
            # "Version X of package logicaloptimizer.cli is not found in NuGet feeds" for a package
            # that is on nuget.org and perfectly installable a minute later - which is exactly how
            # the 3.2.1 release run failed AFTER a completely successful publish.
            $toolAttempts = 10
            $toolDelay = 30
            for ($attempt = 1; $attempt -le $toolAttempts; $attempt++) {
                Write-Host ("==> dotnet tool install LogicalOptimizer.Cli {0} (attempt {1}/{2})" -f $Version, $attempt, $toolAttempts)
                dotnet tool install --global LogicalOptimizer.Cli --version $Version --add-source $nugetSource
                if ($LASTEXITCODE -eq 0) { $script:cliToolInstalled = $true; break }
                if ($attempt -eq $toolAttempts) {
                    throw ("dotnet tool install LogicalOptimizer.Cli {0} failed after {1} attempt(s)" -f $Version, $toolAttempts)
                }
                Write-Host ("    not installable yet; waiting {0}s (nuget.org indexing lag)" -f $toolDelay)
                Start-Sleep -Seconds $toolDelay
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

        # v4.0 (doc/decisions/package-consolidation-v4.md): every forwarding shell must still
        # deliver the FULL assembly set (its single dependency is the consolidated package),
        # so a pre-4.0 consumer who only upgrades the version keeps compiling.
        $allAssemblies = @(
            'LogicalOptimizer', 'LogicalOptimizer.Core', 'LogicalOptimizer.Sat',
            'LogicalOptimizer.Bdd', 'LogicalOptimizer.Dnnf', 'LogicalOptimizer.Formats',
            'LogicalOptimizer.Minimization')
        $modular = @(
            @{ Id = 'LogicalOptimizer.Core';         Assemblies = $allAssemblies }
            @{ Id = 'LogicalOptimizer.Sat';          Assemblies = $allAssemblies }
            @{ Id = 'LogicalOptimizer.Bdd';          Assemblies = $allAssemblies }
            @{ Id = 'LogicalOptimizer.Dnnf';         Assemblies = $allAssemblies }
            @{ Id = 'LogicalOptimizer.Formats';      Assemblies = $allAssemblies }
            @{ Id = 'LogicalOptimizer.Minimization'; Assemblies = $allAssemblies }
            @{ Id = 'LogicalOptimizer.Full';         Assemblies = $allAssemblies }
        )

        foreach ($package in $modular) {
            Test-ModularPackage -PackageId $package.Id -ExpectedAssemblies $package.Assemblies -Root $workDir
        }

        if ($IncludeAot) {
            # The AOT/trim analyzers gating ordinary builds and the in-repo aot.yml both work from a
            # PROJECT REFERENCE. A package can still break AOT (missing trim annotations in the
            # packed assembly, a framework reference that pulls in reflection). Compile the same
            # consumer program natively against the PACKAGE under test (local pre-publish artifacts
            # or the published nuget.org copy — the report records which) to close that gap.
            $rid = if ($isWindowsHost) { 'win-x64' } else { 'linux-x64' }
            $aotOut = Join-Path $workDir 'aot-publish'

            Write-Host "==> dotnet publish -r $rid /p:PublishAot=true (Native AOT from the package)"
            dotnet publish (Join-Path $probe 'Probe.csproj') -c Release -r $rid `
                /p:PublishAot=true /p:InvariantGlobalization=true -o $aotOut
            if ($LASTEXITCODE -ne 0) { throw "Native AOT publish failed with exit code $LASTEXITCODE" }

            $binary = Join-Path $aotOut ($(if ($isWindowsHost) { 'Probe.exe' } else { 'Probe' }))
            if (-not (Test-Path $binary)) { throw "Native AOT publish produced no binary at $binary" }

            Write-Host '==> run the native binary'
            $aotOutput = & $binary 2>&1
            $aotExit = $LASTEXITCODE
            $aotOutput | ForEach-Object { Write-Host "    $_" }
            $aotJoined = $aotOutput -join "`n"

            $sizeBytes = (Get-Item $binary).Length
            $aotOk = $aotExit -eq 0 -and $aotJoined -match 'OK'

            if ($AotReportPath) {
                [ordered]@{
                    reportVersion  = 1
                    tool           = 'tools/smoke_install.ps1 -IncludeAot'
                    version        = $Version
                    runtimeId      = $rid
                    # Provenance must state where the bytes actually came from: local packed
                    # artifacts in the pre-publish gate, nuget.org in the post-publish check.
                    source         = $(if ($nugetSource -eq 'https://api.nuget.org/v3/index.json') {
                                          'published nuget.org package'
                                      } else {
                                          "local packed artifacts (pre-publish): $nugetSource"
                                      })
                    # For the local mode, tie this evidence to the exact bytes that will be
                    # pushed: the consolidated package's SHA-256 must match SHA256SUMS.txt in
                    # the same release. null when testing the published nuget.org copy
                    # (nuget.org repository-signs packages, so its bytes differ by design).
                    consolidatedPackageSha256 = $(
                        $localNupkg = Join-Path $nugetSource "LogicalOptimizer.$Version.nupkg"
                        if ((Test-Path $nugetSource -PathType Container) -and (Test-Path $localNupkg)) {
                            (Get-FileHash $localNupkg -Algorithm SHA256).Hash.ToLowerInvariant()
                        } else { $null })
                    binaryBytes    = $sizeBytes
                    exitCode       = $aotExit
                    result         = $(if ($aotOk) { 'pass' } else { 'fail' })
                    output         = $aotJoined
                    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                } | ConvertTo-Json -Depth 4 | ForEach-Object {
                    # BOM-free so the report parses the same whether produced by Windows
                    # PowerShell 5.1 or pwsh 7.
                    [System.IO.File]::WriteAllText($AotReportPath, $_,
                        (New-Object System.Text.UTF8Encoding($false)))
                }
                Write-Host ("    AOT report written to {0}" -f $AotReportPath)
            }

            if (-not $aotOk) { throw "Native AOT smoke test failed (exit $aotExit)" }
            Write-Host ("    native binary OK ({0:N0} bytes, {1})" -f $sizeBytes, $rid)
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
    # A smoke test must not change the machine it ran on: uninstall the CLI tool this run
    # installed globally (best-effort — a failed uninstall must not mask the real verdict).
    if ($script:cliToolInstalled) {
        Write-Host '==> dotnet tool uninstall LogicalOptimizer.Cli (cleanup)'
        try { dotnet tool uninstall --global LogicalOptimizer.Cli | Out-Null } catch { }
    }
    if (Test-Path $workDir) {
        try { Remove-Item -Recurse -Force $workDir -ErrorAction Stop } catch { }
    }
}
