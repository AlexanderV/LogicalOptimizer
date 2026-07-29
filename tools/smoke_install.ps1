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
      4. installs the CLI as a global tool and asserts its --format=json report;
      5. installs EVERY modular package into its own separate project and asserts that the
         assemblies it should bring load with public types on them - including the full bundle,
         from which all seven library assemblies must be reachable through one reference;
      6. optionally (-IncludeAot) publishes that consumer project with PublishAot=true and runs
         the resulting NATIVE binary, so Native AOT support is proven from the published PACKAGE
         and not only from the in-repo project reference that .github/workflows/aot.yml covers.

    Exits non-zero on the first failure. Safe to run locally against any published version.

.PARAMETER Version
    The published package version to install, e.g. 3.1.0. Required.

.PARAMETER SkipTool
    Skip the global-tool part (useful where the tool path is not on PATH for the session).

.PARAMETER IncludeAot
    Also run the Native AOT publish + native-binary smoke test. Requires a working native
    toolchain (clang + zlib development headers on Linux, MSVC C++ build tools on Windows).

.PARAMETER AotReportPath
    Write a small JSON result for the AOT step here, for inclusion in a release evidence bundle.

.EXAMPLE
    pwsh tools/smoke_install.ps1 -Version 3.1.0

.EXAMPLE
    pwsh tools/smoke_install.ps1 -Version 3.1.0 -IncludeAot -AotReportPath aot-package-smoke.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [switch] $SkipTool,

    [switch] $IncludeAot,

    [string] $AotReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$nugetSource = 'https://api.nuget.org/v3/index.json'

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

        # Every modular package, the facade, and the full bundle - each installed on its own.
        $modular = @(
            @{ Id = 'LogicalOptimizer.Core';         Assemblies = @('LogicalOptimizer.Core') }
            @{ Id = 'LogicalOptimizer.Sat';          Assemblies = @('LogicalOptimizer.Sat') }
            @{ Id = 'LogicalOptimizer.Bdd';          Assemblies = @('LogicalOptimizer.Bdd') }
            @{ Id = 'LogicalOptimizer.Dnnf';         Assemblies = @('LogicalOptimizer.Dnnf') }
            @{ Id = 'LogicalOptimizer.Formats';      Assemblies = @('LogicalOptimizer.Formats') }
            @{ Id = 'LogicalOptimizer.Minimization'; Assemblies = @('LogicalOptimizer.Minimization') }
            # The bundle's whole promise is "one reference, everything available".
            @{ Id = 'LogicalOptimizer.Full';         Assemblies = @(
                    'LogicalOptimizer', 'LogicalOptimizer.Core', 'LogicalOptimizer.Sat',
                    'LogicalOptimizer.Bdd', 'LogicalOptimizer.Dnnf', 'LogicalOptimizer.Formats',
                    'LogicalOptimizer.Minimization') }
        )

        foreach ($package in $modular) {
            Test-ModularPackage -PackageId $package.Id -ExpectedAssemblies $package.Assemblies -Root $workDir
        }

        if ($IncludeAot) {
            # The AOT/trim analyzers gating ordinary builds and the in-repo aot.yml both work from a
            # PROJECT REFERENCE. A package can still break AOT (missing trim annotations in the
            # packed assembly, a framework reference that pulls in reflection). Compile the same
            # consumer program natively against the PUBLISHED package to close that gap.
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
                    source         = 'published nuget.org package'
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
    if (Test-Path $workDir) {
        try { Remove-Item -Recurse -Force $workDir -ErrorAction Stop } catch { }
    }
}
