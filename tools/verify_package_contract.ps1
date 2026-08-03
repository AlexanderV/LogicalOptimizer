#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Audit the CONTENTS of the packed LogicalOptimizer .nupkg/.snupkg files against the package
    contract, and write a machine-readable verification report.

.DESCRIPTION
    tools/verify_nuget.ps1 proves a package is present in the nuget.org index; tools/smoke_install.ps1
    proves it installs and runs. Neither looks INSIDE the package, so a package could ship with a
    missing README, a stale description, no license expression, no symbols, or an unexpected
    third-party dependency and still pass both. This script opens every .nupkg as a zip archive,
    reads its .nuspec, and asserts the contract:

      * the expected nine packages are present at the requested version, and nothing else is;
      * a package-specific README is declared AND actually present in the package;
      * Description is non-empty, substantial, and distinct from every other package's;
      * PackageTags are present;
      * PackageProjectUrl and repository url/type/commit metadata are present and as expected;
      * the license is declared as an SPDX expression (no ambiguous license file/URL);
      * symbols ship as an adjacent .snupkg containing a .pdb;
      * every dependency is a LogicalOptimizer package - no third-party runtime dependency;
      * the expected target frameworks are actually in lib/ (or tools/ for the CLI tool, and
        none for the dependency-only meta-package);
      * the CLI package declares packageType DotnetTool.

    Runs entirely offline against local files - no nuget.org access - so it can gate `dotnet pack`
    in CI before anything is pushed, and can be re-run against downloaded packages afterwards.

.PARAMETER ArtifactsPath
    Directory containing the packed .nupkg/.snupkg files. Default 'artifacts'.

.PARAMETER Version
    The package version to audit, e.g. 3.1.0. Required.

.PARAMETER ReportPath
    Where to write the JSON verification report. Default 'package-contract-report.json'.

.PARAMETER SkipRepositoryCommit
    Do not require a repository commit SHA in the nuspec. The SDK only stamps it when packing from
    a git checkout with SourceLink enabled; use this when auditing a package built elsewhere.

.EXAMPLE
    dotnet pack LogicalOptimizer.sln -c Release -o artifacts /p:Version=3.1.0
    pwsh tools/verify_package_contract.ps1 -Version 3.1.0

.EXAMPLE
    # Audit already-published packages: download them first, then run the same contract.
    pwsh tools/verify_package_contract.ps1 -ArtifactsPath downloaded -Version 3.1.0
#>
[CmdletBinding()]
param(
    [string] $ArtifactsPath = 'artifacts',

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [string] $ReportPath = 'package-contract-report.json',

    [switch] $SkipRepositoryCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedProjectUrl = 'https://AlexanderV.github.io/LogicalOptimizer/'
$expectedRepositoryUrl = 'https://github.com/AlexanderV/LogicalOptimizer.git'
$expectedLicense = 'Apache-2.0'
$minimumDescriptionLength = 60
$minimumTagCount = 4

# The v4.0 package matrix (doc/decisions/package-consolidation-v4.md): ONE real library
# package carrying all seven assemblies, the CLI tool, and seven deprecated forwarding
# shells that keep the pre-4.0 IDs installable during the transition period.
#   Kind 'library'    -> the contracted assemblies under lib/<tfm>/
#   Kind 'tool'       -> a .NET tool under tools/<tfm>/any/
#   Kind 'forwarding' -> no assemblies; exactly one dependency on LogicalOptimizer
$libraryAssemblies = @(
    'LogicalOptimizer', 'LogicalOptimizer.Core', 'LogicalOptimizer.Sat',
    'LogicalOptimizer.Bdd', 'LogicalOptimizer.Dnnf', 'LogicalOptimizer.Formats',
    'LogicalOptimizer.Minimization')
$contract = @(
    @{ Id = 'LogicalOptimizer';              Kind = 'library';    Frameworks = @('net8.0'); Assemblies = $libraryAssemblies }
    @{ Id = 'LogicalOptimizer.Cli';          Kind = 'tool';       Frameworks = @('net10.0') }
    @{ Id = 'LogicalOptimizer.Core';         Kind = 'forwarding'; Frameworks = @() }
    @{ Id = 'LogicalOptimizer.Sat';          Kind = 'forwarding'; Frameworks = @() }
    @{ Id = 'LogicalOptimizer.Bdd';          Kind = 'forwarding'; Frameworks = @() }
    @{ Id = 'LogicalOptimizer.Dnnf';         Kind = 'forwarding'; Frameworks = @() }
    @{ Id = 'LogicalOptimizer.Formats';      Kind = 'forwarding'; Frameworks = @() }
    @{ Id = 'LogicalOptimizer.Minimization'; Kind = 'forwarding'; Frameworks = @() }
    @{ Id = 'LogicalOptimizer.Full';         Kind = 'forwarding'; Frameworks = @() }
)

# ---------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------

# Under Set-StrictMode, dotted access to an absent XML element throws instead of yielding $null,
# so every nuspec read goes through this.
function Get-MetaValue {
    param(
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)] [string] $Name
    )
    if ($null -eq $Metadata) { return $null }
    $node = $Metadata.SelectSingleNode("*[local-name()='$Name']")
    if ($null -eq $node) { return $null }
    return $node
}

function Get-MetaText {
    param(
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)] [string] $Name
    )
    $node = Get-MetaValue -Metadata $Metadata -Name $Name
    if ($null -eq $node) { return $null }
    return [string]$node.InnerText
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)] $Archive,
        [Parameter(Mandatory = $true)] [string] $EntryName
    )
    $entry = $Archive.Entries | Where-Object { $_.FullName -eq $EntryName } | Select-Object -First 1
    if ($null -eq $entry) { return $null }
    $stream = $entry.Open()
    try {
        $reader = New-Object System.IO.StreamReader($stream)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

$script:packageReports = [System.Collections.Generic.List[object]]::new()
$script:globalChecks = [System.Collections.Generic.List[object]]::new()

function New-Check {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [bool] $Ok,
        [string] $Detail = ''
    )
    return [ordered]@{
        name   = $Name
        status = if ($Ok) { 'pass' } else { 'fail' }
        detail = $Detail
    }
}

# ---------------------------------------------------------------------------------------------
# Audit
# ---------------------------------------------------------------------------------------------

$resolvedArtifacts = if (Test-Path $ArtifactsPath) { (Resolve-Path $ArtifactsPath).Path } else { $ArtifactsPath }

Write-Host ("Auditing package contract for version {0}" -f $Version)
Write-Host ("Artifacts: {0}" -f $resolvedArtifacts)
Write-Host ''

if (-not (Test-Path $resolvedArtifacts)) {
    Write-Error ("Artifacts directory not found: {0}" -f $resolvedArtifacts)
    exit 1
}

$allNupkgs = @(Get-ChildItem -Path $resolvedArtifacts -Filter '*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' })
$expectedFileNames = $contract | ForEach-Object { "$($_.Id).$Version.nupkg" }
$unexpected = @($allNupkgs | Where-Object { $expectedFileNames -notcontains $_.Name } | ForEach-Object { $_.Name })

$script:globalChecks.Add((New-Check -Name 'no-unexpected-packages' -Ok ($unexpected.Count -eq 0) `
    -Detail $(if ($unexpected.Count -eq 0) {
            "only the $($contract.Count) contracted packages are present"
        } else {
            "unexpected package file(s): $($unexpected -join ', ')"
        })))

$descriptions = @{}
$dependencyMap = @{}

foreach ($entry in $contract) {
    $id = $entry.Id
    $checks = [System.Collections.Generic.List[object]]::new()
    $nupkgName = "$id.$Version.nupkg"
    $nupkgPath = Join-Path $resolvedArtifacts $nupkgName
    $snupkgPath = Join-Path $resolvedArtifacts "$id.$Version.snupkg"

    Write-Host ("-- {0}" -f $id)

    if (-not (Test-Path $nupkgPath)) {
        $checks.Add((New-Check -Name 'package-present' -Ok $false -Detail "missing $nupkgName"))
        $script:packageReports.Add([ordered]@{ id = $id; kind = $entry.Kind; file = $nupkgName; checks = @($checks) })
        Write-Host "   FAIL package-present: missing $nupkgName"
        continue
    }

    $checks.Add((New-Check -Name 'package-present' -Ok $true -Detail $nupkgName))

    $archive = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try {
        $files = @($archive.Entries | ForEach-Object { $_.FullName })
        $nuspecText = Read-ZipEntryText -Archive $archive -EntryName "$id.nuspec"

        if ($null -eq $nuspecText) {
            $checks.Add((New-Check -Name 'nuspec-readable' -Ok $false -Detail "no $id.nuspec in the package"))
            $script:packageReports.Add([ordered]@{ id = $id; kind = $entry.Kind; file = $nupkgName; checks = @($checks) })
            continue
        }

        $checks.Add((New-Check -Name 'nuspec-readable' -Ok $true -Detail "$id.nuspec parsed"))

        $nuspec = [xml]$nuspecText
        $metadata = $nuspec.DocumentElement.SelectSingleNode("*[local-name()='metadata']")

        # --- identity ---------------------------------------------------------------------
        $packedId = Get-MetaText -Metadata $metadata -Name 'id'
        $checks.Add((New-Check -Name 'id-matches' -Ok ($packedId -eq $id) -Detail "id=$packedId"))

        $packedVersion = Get-MetaText -Metadata $metadata -Name 'version'
        $checks.Add((New-Check -Name 'version-matches' -Ok ($packedVersion -eq $Version) `
            -Detail "version=$packedVersion, expected=$Version"))

        # --- discoverability metadata ----------------------------------------------------
        $description = Get-MetaText -Metadata $metadata -Name 'description'
        $descriptionOk = -not [string]::IsNullOrWhiteSpace($description) -and
                         $description.Trim().Length -ge $minimumDescriptionLength
        $checks.Add((New-Check -Name 'description-substantial' -Ok $descriptionOk `
            -Detail $(if ($null -eq $description) { 'no <description>' }
                      else { "$($description.Trim().Length) chars (minimum $minimumDescriptionLength)" })))
        if (-not [string]::IsNullOrWhiteSpace($description)) { $descriptions[$id] = $description.Trim() }

        $tagsRaw = Get-MetaText -Metadata $metadata -Name 'tags'
        $tags = @()
        if (-not [string]::IsNullOrWhiteSpace($tagsRaw)) {
            $tags = @($tagsRaw -split '[\s;]+' | Where-Object { $_ })
        }
        $checks.Add((New-Check -Name 'tags-present' -Ok ($tags.Count -ge $minimumTagCount) `
            -Detail "$($tags.Count) tag(s) (minimum $minimumTagCount): $($tags -join ', ')"))

        $projectUrl = Get-MetaText -Metadata $metadata -Name 'projectUrl'
        $checks.Add((New-Check -Name 'project-url' -Ok ($projectUrl -eq $expectedProjectUrl) `
            -Detail "projectUrl=$projectUrl"))

        # --- license ---------------------------------------------------------------------
        $license = Get-MetaValue -Metadata $metadata -Name 'license'
        $licenseType = if ($null -ne $license) { [string]$license.type } else { $null }
        $licenseValue = if ($null -ne $license) { [string]$license.InnerText } else { $null }
        $licenseOk = $licenseType -eq 'expression' -and $licenseValue -eq $expectedLicense
        $checks.Add((New-Check -Name 'license-expression' -Ok $licenseOk `
            -Detail "type=$licenseType, value=$licenseValue, expected expression/$expectedLicense"))

        # --- README ----------------------------------------------------------------------
        $readme = Get-MetaText -Metadata $metadata -Name 'readme'
        $readmeDeclared = -not [string]::IsNullOrWhiteSpace($readme)
        $checks.Add((New-Check -Name 'readme-declared' -Ok $readmeDeclared `
            -Detail $(if ($readmeDeclared) { "<readme>$readme</readme>" } else { 'no <readme> element' })))

        if ($readmeDeclared) {
            $normalized = $readme -replace '\\', '/'
            $readmeEntry = $archive.Entries |
                Where-Object { $_.FullName -eq $normalized } | Select-Object -First 1
            if ($null -eq $readmeEntry) {
                $checks.Add((New-Check -Name 'readme-file-present' -Ok $false `
                    -Detail "declared '$normalized' is not in the package"))
            }
            else {
                # An empty or stub README is as good as none for nuget.org's package page.
                $readmeText = Read-ZipEntryText -Archive $archive -EntryName $normalized
                $readmeLength = if ($null -eq $readmeText) { 0 } else { $readmeText.Trim().Length }
                $checks.Add((New-Check -Name 'readme-file-present' -Ok ($readmeLength -ge 200) `
                    -Detail "$normalized is $readmeLength chars (minimum 200)"))
                # A package-specific README must actually name its own package.
                $mentionsId = $null -ne $readmeText -and $readmeText.Contains($id)
                $checks.Add((New-Check -Name 'readme-is-package-specific' -Ok $mentionsId `
                    -Detail $(if ($mentionsId) { "mentions $id" } else { "does not mention $id" })))
            }
        }

        # --- repository / SourceLink metadata --------------------------------------------
        $repository = Get-MetaValue -Metadata $metadata -Name 'repository'
        $repoUrl = if ($null -ne $repository) { [string]$repository.url } else { $null }
        $repoType = if ($null -ne $repository) { [string]$repository.type } else { $null }
        $repoCommit = if ($null -ne $repository -and
                          ($repository.PSObject.Properties.Name -contains 'commit')) {
            [string]$repository.commit
        } else { $null }

        $checks.Add((New-Check -Name 'repository-metadata' `
            -Ok ($repoUrl -eq $expectedRepositoryUrl -and $repoType -eq 'git') `
            -Detail "type=$repoType, url=$repoUrl"))

        # Forwarding shells pack from a hand-written nuspec, so the SDK never stamps a
        # SourceLink commit into them - and there are no sources to resolve anyway.
        if (-not $SkipRepositoryCommit -and $entry.Kind -ne 'forwarding') {
            $commitOk = $repoCommit -match '^[0-9a-f]{40}$'
            $checks.Add((New-Check -Name 'repository-commit' -Ok ([bool]$commitOk) `
                -Detail $(if ($commitOk) { "commit=$repoCommit" }
                          else { "commit='$repoCommit' is not a 40-hex SHA (SourceLink needs it to resolve sources)" })))
        }

        # --- dependencies: nothing third-party -------------------------------------------
        $dependencyIds = @($nuspec.SelectNodes("//*[local-name()='dependency']") |
            ForEach-Object { [string]$_.id } | Where-Object { $_ } | Sort-Object -Unique)
        $dependencyMap[$id] = $dependencyIds
        $foreign = @($dependencyIds | Where-Object { $_ -notlike 'LogicalOptimizer*' })
        $checks.Add((New-Check -Name 'no-third-party-dependencies' -Ok ($foreign.Count -eq 0) `
            -Detail $(if ($foreign.Count -eq 0) {
                    "$($dependencyIds.Count) dependency(ies), all LogicalOptimizer: $($dependencyIds -join ', ')"
                } else {
                    "third-party dependency(ies): $($foreign -join ', ')"
                })))

        # --- payload: the frameworks the contract promises --------------------------------
        switch ($entry.Kind) {
            'library' {
                $missing = @()
                foreach ($tfm in $entry.Frameworks) {
                    if (-not ($files | Where-Object { $_ -like "lib/$tfm/*.dll" })) { $missing += $tfm }
                }
                $checks.Add((New-Check -Name 'expected-target-frameworks' -Ok ($missing.Count -eq 0) `
                    -Detail $(if ($missing.Count -eq 0) {
                            "lib/ assemblies for $($entry.Frameworks -join ', ')"
                        } else {
                            "no assembly under lib/ for: $($missing -join ', ')"
                        })))

                $extraTfms = @($files | Where-Object { $_ -like 'lib/*/*' } |
                    ForEach-Object { ($_ -split '/')[1] } | Sort-Object -Unique |
                    Where-Object { $entry.Frameworks -notcontains $_ })
                $checks.Add((New-Check -Name 'no-unexpected-target-frameworks' -Ok ($extraTfms.Count -eq 0) `
                    -Detail $(if ($extraTfms.Count -eq 0) { 'lib/ contains only contracted frameworks' }
                              else { "undocumented framework(s) in lib/: $($extraTfms -join ', ')" })))

                # XML docs are what the docs site and IntelliSense are built from.
                $missingDocs = @()
                foreach ($tfm in $entry.Frameworks) {
                    if (-not ($files | Where-Object { $_ -like "lib/$tfm/*.xml" })) { $missingDocs += $tfm }
                }
                $checks.Add((New-Check -Name 'xml-documentation' -Ok ($missingDocs.Count -eq 0) `
                    -Detail $(if ($missingDocs.Count -eq 0) { 'XML docs present for every framework' }
                              else { "no XML doc file for: $($missingDocs -join ', ')" })))

                # The single-package contract: every one of the seven assemblies (and its XML
                # doc) must be inside lib/ - a dropped companion assembly would surface only
                # as a consumer's MissingMethodException otherwise.
                if ($entry.ContainsKey('Assemblies')) {
                    $missingAssemblies = @()
                    foreach ($tfm in $entry.Frameworks) {
                        foreach ($asm in $entry.Assemblies) {
                            if ($files -notcontains "lib/$tfm/$asm.dll") { $missingAssemblies += "$asm.dll ($tfm)" }
                            if ($files -notcontains "lib/$tfm/$asm.xml") { $missingAssemblies += "$asm.xml ($tfm)" }
                        }
                    }
                    $checks.Add((New-Check -Name 'bundled-assemblies-complete' -Ok ($missingAssemblies.Count -eq 0) `
                        -Detail $(if ($missingAssemblies.Count -eq 0) {
                                "all $($entry.Assemblies.Count) assemblies + XML docs in lib/"
                            } else {
                                "missing from lib/: $($missingAssemblies -join ', ')"
                            })))
                }
            }
            'tool' {
                $tfm = $entry.Frameworks[0]
                $hasTool = [bool]($files | Where-Object { $_ -like "tools/$tfm/any/*.dll" })
                $checks.Add((New-Check -Name 'expected-target-frameworks' -Ok $hasTool `
                    -Detail $(if ($hasTool) { "tools/$tfm/any/ payload present" }
                              else { "no tools/$tfm/any/*.dll in the package" })))

                $hasSettings = [bool]($files | Where-Object { $_ -like '*DotnetToolSettings.xml' })
                $checks.Add((New-Check -Name 'dotnet-tool-settings' -Ok $hasSettings `
                    -Detail $(if ($hasSettings) { 'DotnetToolSettings.xml present' }
                              else { 'DotnetToolSettings.xml missing - `dotnet tool install` cannot register a command' })))

                $packageTypes = @($nuspec.SelectNodes("//*[local-name()='packageType']") |
                    ForEach-Object { [string]$_.name })
                $checks.Add((New-Check -Name 'dotnet-tool-package-type' `
                    -Ok ($packageTypes -contains 'DotnetTool') `
                    -Detail "packageType(s): $($packageTypes -join ', ')"))
            }
            'forwarding' {
                $hasLib = [bool]($files | Where-Object { $_ -like 'lib/*' })
                $checks.Add((New-Check -Name 'no-assemblies-in-forwarding-package' -Ok (-not $hasLib) `
                    -Detail $(if ($hasLib) { 'forwarding package unexpectedly ships assemblies' }
                              else { 'no assemblies, as documented' })))

                # The whole contract of a forwarding shell: exactly one dependency, and it is
                # the consolidated package pinned to EXACTLY this version — "[X]" in NuGet
                # range syntax. A bare "X" would mean ">= X" and let a 4.0.0 shell resolve to
                # any newer LogicalOptimizer, which is not what the shell's version promises.
                $depNodes = @($nuspec.SelectNodes("//*[local-name()='dependency']"))
                $forwardOk = $depNodes.Count -eq 1 -and
                             [string]$depNodes[0].id -eq 'LogicalOptimizer' -and
                             [string]$depNodes[0].version -eq "[$Version]"
                $checks.Add((New-Check -Name 'forwards-to-consolidated-package' -Ok $forwardOk `
                    -Detail $(if ($forwardOk) { "single dependency: LogicalOptimizer [$Version] (exact)" }
                              else {
                                  "expected exactly one dependency 'LogicalOptimizer [$Version]', got: " +
                                  (($depNodes | ForEach-Object { "$([string]$_.id) $([string]$_.version)" }) -join ', ')
                              })))

                $deprecatedOk = $null -ne $description -and $description.Contains('DEPRECATED')
                $checks.Add((New-Check -Name 'deprecation-is-explicit' -Ok $deprecatedOk `
                    -Detail $(if ($deprecatedOk) { 'description says DEPRECATED' }
                              else { 'description does not say DEPRECATED' })))
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    # --- symbols ------------------------------------------------------------------------------
    # Whatever the package kind, a .snupkg that exists must not be EMPTY: nuget.org rejects a
    # symbol package containing no .pdb with 400, and because `dotnet nuget push` walks the glob
    # in order and aborts on the first failure, one such package halts the publish part-way
    # through - leaving some packages live and the rest missing. That is exactly how 3.2.0
    # shipped 3 of 9 (the code-less meta-package inherited IncludeSymbols from
    # Directory.Build.props and packed an empty .snupkg). The per-kind contract below only ever
    # asserted that libraries HAVE pdbs; nothing checked the packages it skipped, so the
    # pre-publish gate passed on a package nuget.org was going to refuse.
    if (Test-Path $snupkgPath) {
        $anySymbols = [System.IO.Compression.ZipFile]::OpenRead($snupkgPath)
        try {
            $anyPdb = @($anySymbols.Entries | Where-Object { $_.FullName -like '*.pdb' }).Count
            $checks.Add((New-Check -Name 'symbols-package-not-empty' -Ok ($anyPdb -gt 0) `
                -Detail $(if ($anyPdb -gt 0) { "$anyPdb pdb(s) present" }
                          else { "$id.$Version.snupkg contains no .pdb - nuget.org will reject it with 400 and abort the publish" })))
        }
        finally {
            $anySymbols.Dispose()
        }
    }

    # The tool package is an executable, not a referenced library; symbols are only contracted
    # for the library packages a consumer can step into.
    if ($entry.Kind -eq 'library') {
        if (-not (Test-Path $snupkgPath)) {
            $checks.Add((New-Check -Name 'symbols-package' -Ok $false `
                -Detail "missing $id.$Version.snupkg"))
        }
        else {
            $symbols = [System.IO.Compression.ZipFile]::OpenRead($snupkgPath)
            try {
                $pdbs = @($symbols.Entries | Where-Object { $_.FullName -like '*.pdb' } |
                    ForEach-Object { $_.FullName })
                $missingPdbs = @()
                foreach ($tfm in $entry.Frameworks) {
                    if (-not ($pdbs | Where-Object { $_ -like "lib/$tfm/*.pdb" })) { $missingPdbs += $tfm }
                }
                $checks.Add((New-Check -Name 'symbols-package' -Ok ($missingPdbs.Count -eq 0) `
                    -Detail $(if ($missingPdbs.Count -eq 0) {
                            "$($pdbs.Count) pdb(s) covering $($entry.Frameworks -join ', ')"
                        } else {
                            "no pdb under lib/ for: $($missingPdbs -join ', ')"
                        })))
            }
            finally {
                $symbols.Dispose()
            }
        }
    }

    foreach ($c in $checks) {
        if ($c.status -eq 'pass') { Write-Host ("   ok   {0}: {1}" -f $c.name, $c.detail) }
        else { Write-Host ("   FAIL {0}: {1}" -f $c.name, $c.detail) }
    }

    $script:packageReports.Add([ordered]@{
        id     = $id
        kind   = $entry.Kind
        file   = $nupkgName
        checks = @($checks)
    })
}

# Pre-4.0 the Full meta-package needed a transitive-closure check ("does the bundle really
# reach every library?"). Since v4.0 the consolidated LogicalOptimizer package IS the bundle
# and its completeness is asserted directly by 'bundled-assemblies-complete'; every
# forwarding shell is pinned to it by 'forwards-to-consolidated-package'.

# A copy-pasted description makes every package look the same on nuget.org, which is the exact
# discoverability problem per-package metadata is meant to solve.
$duplicateGroups = @($descriptions.GetEnumerator() | Group-Object -Property Value |
    Where-Object { $_.Count -gt 1 })
$script:globalChecks.Add((New-Check -Name 'descriptions-are-distinct' -Ok ($duplicateGroups.Count -eq 0) `
    -Detail $(if ($duplicateGroups.Count -eq 0) {
            "$($descriptions.Count) package description(s), all distinct"
        } else {
            "shared description across: " + (($duplicateGroups | ForEach-Object {
                ($_.Group | ForEach-Object { $_.Key }) -join '/'
            }) -join '; ')
        })))

# ---------------------------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------------------------

$allChecks = @($script:globalChecks) + @($script:packageReports | ForEach-Object { $_.checks } | ForEach-Object { $_ })
$failed = @($allChecks | Where-Object { $_.status -eq 'fail' })

$report = [ordered]@{
    reportVersion  = 1
    tool           = 'tools/verify_package_contract.ps1'
    version        = $Version
    artifactsPath  = $resolvedArtifacts
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    summary        = [ordered]@{
        packages      = $script:packageReports.Count
        checks        = $allChecks.Count
        failed        = $failed.Count
        result        = if ($failed.Count -eq 0) { 'pass' } else { 'fail' }
    }
    # What this report does NOT prove, so nobody over-reads it.
    limitations    = @(
        'Repository url/commit and a .pdb in the .snupkg are necessary for SourceLink but not sufficient; stepping into sources is not exercised here.',
        'Package contents only - installability and runtime behaviour are covered by tools/smoke_install.ps1.'
    )
    globalChecks   = @($script:globalChecks)
    packages       = @($script:packageReports)
}

$reportDirectory = Split-Path -Parent $ReportPath
if ($reportDirectory -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}
# .NET file APIs resolve relative paths against the process directory, not PowerShell's location.
if (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = Join-Path (Get-Location).Path $ReportPath
}
# Windows PowerShell 5.1's `Out-File -Encoding utf8` writes a BOM, which trips strict JSON
# parsers; pwsh 7 does not. Write BOM-free so the report is identical on both.
[System.IO.File]::WriteAllText($ReportPath, ($report | ConvertTo-Json -Depth 8),
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host '==== Package contract summary ===='
Write-Host ("  packages: {0}" -f $report.summary.packages)
Write-Host ("  checks:   {0}" -f $report.summary.checks)
Write-Host ("  failed:   {0}" -f $report.summary.failed)
Write-Host ("  report:   {0}" -f $ReportPath)
Write-Host '=================================='

if ($failed.Count -gt 0) {
    Write-Host ''
    foreach ($c in $failed) { Write-Host ("  FAIL {0}: {1}" -f $c.name, $c.detail) }
    Write-Error ("Package contract violated: {0} check(s) failed. See {1}." -f $failed.Count, $ReportPath)
    exit 1
}

Write-Host ("Package contract satisfied for version {0}." -f $Version)
exit 0
