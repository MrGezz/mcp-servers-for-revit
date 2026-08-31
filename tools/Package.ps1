<#
    Package.ps1 - build the release payload for every Revit version.

    The GitHub release workflow already zips `plugin/bin/AddIn <year> <config>`
    per year. This does the same thing locally, so a release can be produced and
    INSPECTED without pushing a tag and waiting for CI, and so Make-Installer.ps1
    has a staged payload to wrap.

    WHAT IT PRODUCES

        dist/
          mcp-servers-for-revit <version>/
            Revit2020/ ... Revit2027/        <- one deployable add-in tree each
            READ ME FIRST.txt
          mcp-servers-for-revit-v<version>-Revit<year>.zip   (one per year)

    Each Revit<year> folder is exactly what the README tells a user to drop into
    %AppData%\Autodesk\Revit\Addins\<year>\ - the .addin manifest beside the
    revit_mcp_plugin\ tree - so the zip contents and the documented layout cannot
    drift apart.

    VERSION comes from server/package.json. That file already has to carry the
    version for npm, and a second copy in a .props file is a second thing to
    forget to bump.

    PDBs ARE STRIPPED. They are debug symbols, the user has no use for them, and
    RevitMCPPlugin.pdb has been shipping in every release ZIP to date.

    Usage:
        powershell -ExecutionPolicy Bypass -File .\tools\Package.ps1
        powershell -ExecutionPolicy Bypass -File .\tools\Package.ps1 -Years 2026,2027
        powershell -ExecutionPolicy Bypass -File .\tools\Package.ps1 -SkipBuild -NoZip
#>

[CmdletBinding()]
param(
    [string]   $Configuration = 'Release',
    [int[]]    $Years,
    [string]   $Version,
    [switch]   $SkipBuild,
    [switch]   $NoZip,
    [switch]   $KeepDuplicateAssemblies
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Step { param([string] $T) Write-Host "==> $T" -ForegroundColor Cyan }
function Ok   { param([string] $T) Write-Host "    $T" -ForegroundColor Green }
function Warn { param([string] $T) Write-Host "    $T" -ForegroundColor Yellow }
function Note { param([string] $T) Write-Host "    $T" -ForegroundColor DarkGray }

# Every Revit version this repository has a build configuration for. Keep in
# step with <Configurations> in both .csproj files; Verify.ps1 asserts they agree.
$AllYears = @(2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027)
if (-not $Years) { $Years = $AllYears }

foreach ($y in $Years) {
    if ($AllYears -notcontains $y) {
        throw "Revit $y has no build configuration. Known: $($AllYears -join ', ')."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on PATH. Packaging needs it; installing the result does not."
}

if (-not $Version) {
    $pkg = Join-Path $repo 'server\package.json'
    if (-not (Test-Path $pkg)) { throw "Cannot determine the version: $pkg is missing. Pass -Version." }
    $Version = (Get-Content $pkg -Raw | ConvertFrom-Json).version
}
if (-not $Version) { throw 'server/package.json has no "version". Pass -Version.' }

$stage = Join-Path $repo "dist\mcp-servers-for-revit $Version"

Write-Host ''
Write-Host "mcp-servers-for-revit - package $Version" -ForegroundColor Cyan
Write-Host "Revit $($Years -join ', ')" -ForegroundColor Cyan
Write-Host '--------------------------------------------------' -ForegroundColor Cyan

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$summary = @()

foreach ($year in $Years) {
    $config = "$Configuration R$($year.ToString().Substring(2))"

    # ------------------------------------------------------------------ build
    if (-not $SkipBuild) {
        Step "Building Revit $year  ($config)"

        # PublishAddinFiles=false is not cosmetic. The Nice3point build task
        # otherwise publishes the command set straight into
        # %AppData%\Autodesk\Revit\Addins\<year>\, i.e. it INSTALLS into the
        # developer's live Revit as a side effect of packaging. Building a
        # release must not touch the machine's own add-in folders.
        $log = & dotnet build (Join-Path $repo 'mcp-servers-for-revit.sln') `
                    -c $config -v quiet --nologo -p:PublishAddinFiles=false 2>&1
        if ($LASTEXITCODE -ne 0) {
            $log | Select-String -Pattern ': error ' | Select-Object -First 10 |
                ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            throw "Build failed for $config (exit $LASTEXITCODE)."
        }
    }

    # ------------------------------------------------------------------ stage
    $built = Join-Path $repo "plugin\bin\AddIn $year $config"
    if (-not (Test-Path $built)) {
        throw "Expected build output at '$built' but it is not there. Drop -SkipBuild, or check the configuration name."
    }

    $dest = Join-Path $stage "Revit$year"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item (Join-Path $built '*') $dest -Recurse -Force

    # -------------------------------------------------------------- assertions
    # Assert the SHAPE, not just that something was copied. Every one of these
    # has a failure mode that is silent until a user's Revit does not load the
    # add-in, at which point it is a support thread rather than a build error.
    $mustExist = @(
        'mcp-servers-for-revit.addin',
        'revit_mcp_plugin\RevitMCPPlugin.dll',
        'revit_mcp_plugin\Commands\RevitMCPCommandSet\command.json',
        "revit_mcp_plugin\Commands\RevitMCPCommandSet\$year\RevitMCPCommandSet.dll"
    )
    foreach ($rel in $mustExist) {
        if (-not (Test-Path -LiteralPath (Join-Path $dest $rel))) {
            throw "Revit ${year}: the payload is missing '$rel'. The add-in would not load."
        }
    }

    # The manifest's <Assembly> is resolved relative to the .addin file. If the
    # two ever drift, Revit reports only "cannot run the external application".
    [xml] $manifest = Get-Content (Join-Path $dest 'mcp-servers-for-revit.addin')
    $asmRel = $manifest.RevitAddIns.AddIn.Assembly
    if (-not (Test-Path -LiteralPath (Join-Path $dest ($asmRel -replace '/', '\')))) {
        throw "Revit ${year}: the manifest points at '$asmRel', which is not in the payload."
    }

    # Exactly one .addin. Two manifests carrying the same ClientId in one folder
    # is what made Revit refuse the add-in in v1.0.0 (issue #12): the second is
    # rejected with "client id ... is already loaded in session", which surfaces
    # as a load failure for the whole add-in.
    $addins = @(Get-ChildItem $dest -Filter *.addin -File)
    if ($addins.Count -ne 1) {
        throw "Revit ${year}: expected exactly one .addin in the payload, found $($addins.Count): $($addins.Name -join ', ')."
    }

    # ------------------------------------------------------------------ trim
    $pdbs = @(Get-ChildItem $dest -Filter *.pdb -Recurse -File)
    if ($pdbs) { $pdbs | Remove-Item -Force }

    # The plugin folder and the command-set folder each carry their own copy of
    # the shared assemblies. Measured across all eight years they are BYTE
    # IDENTICAL, so the duplication is waste rather than the version conflict it
    # has been mistaken for - but it is not removed by default, because the
    # command set is loaded with Assembly.LoadFrom from its own directory and
    # relies on resolving its dependencies there.
    $dupBytes = 0
    $pluginDir = Join-Path $dest 'revit_mcp_plugin'
    $cmdDir    = Join-Path $pluginDir "Commands\RevitMCPCommandSet\$year"
    foreach ($dll in Get-ChildItem $cmdDir -Filter *.dll -File) {
        $twin = Join-Path $pluginDir $dll.Name
        if (Test-Path -LiteralPath $twin) {
            $a = (Get-FileHash $dll.FullName -Algorithm SHA256).Hash
            $b = (Get-FileHash $twin        -Algorithm SHA256).Hash
            if ($a -eq $b) {
                $dupBytes += $dll.Length
                if ($KeepDuplicateAssemblies) { continue }
            } else {
                # This is the case issue #48 hypothesised. It has never been
                # observed in a from-source build, so if it ever fires, say so
                # loudly rather than shipping it.
                Warn "Revit ${year}: '$($dll.Name)' DIFFERS between the plugin and command-set folders. That is the mismatch #48 describes."
            }
        }
    }

    $files = @(Get-ChildItem $dest -Recurse -File)
    $mb    = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)

    $summary += [pscustomobject]@{
        Year      = $year
        Config    = $config
        Files     = $files.Count
        SizeMB    = $mb
        DupMB     = [math]::Round($dupBytes / 1MB, 1)
        PdbsCut   = $pdbs.Count
    }
    Ok "Revit $year  -  $($files.Count) files, $mb MB"
}

# ------------------------------------------------------------------ server
# The Revit add-in is only half the product. Staging the compiled Node server
# here is what lets the installer register a client config that points at THIS
# install rather than at whatever "npx -y mcp-server-for-revit" resolves to.
#
# node_modules is shipped (production only). That is safe ONLY because the
# server has no native dependency any more - better-sqlite3 was the last one,
# and it went when project data moved into Revit Extensible Storage. A native
# module here would be compiled against the PACKAGER's Node ABI and would fail
# on a user with a different Node major version, so the assertion below is a
# real gate, not decoration.
Step 'Staging MCP server'

$serverSrc = Join-Path $repo 'server'
$serverOut = Join-Path $stage 'server'

if (-not $SkipBuild) {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw "npm was not found on PATH. It is needed to build and stage the MCP server. Use -SkipBuild to package the add-in alone."
    }
    Push-Location $serverSrc
    try {
        # --omit=dev so typescript and the type packages do not ship.
        & npm install --omit=dev --no-audit --no-fund 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)." }
        # The build needs the dev dependency tsc, so restore the full tree, build,
        # then prune back to production before copying.
        & npm install --no-audit --no-fund 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "npm install (full) failed (exit $LASTEXITCODE)." }
        & npm run build 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit $LASTEXITCODE)." }
        & npm prune --omit=dev 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "npm prune failed (exit $LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

$entry = Join-Path $serverSrc 'build\index.js'
if (-not (Test-Path $entry)) {
    throw "The MCP server has not been built: '$entry' is missing. Drop -SkipBuild, or run npm run build in server\."
}

New-Item -ItemType Directory -Path $serverOut -Force | Out-Null
Copy-Item (Join-Path $serverSrc 'build') $serverOut -Recurse -Force
Copy-Item (Join-Path $serverSrc 'package.json') $serverOut -Force
$nodeModules = Join-Path $serverSrc 'node_modules'
if (Test-Path $nodeModules) {
    Copy-Item $nodeModules $serverOut -Recurse -Force
}

# GATE: a native module would be ABI-locked to the packaging machine's Node.
$native = @(Get-ChildItem $serverOut -Recurse -Filter *.node -ErrorAction SilentlyContinue)
if ($native.Count -gt 0) {
    throw ("The staged server contains $($native.Count) native module(s), e.g. '$($native[0].Name)'. " +
           'These are compiled against one Node ABI and will fail for users on a different Node major version. ' +
           'Remove the native dependency, or stop shipping node_modules and require the user to run npm install.')
}

# GATE: the registration script the installer runs must be present.
$targetScript = Join-Path $PSScriptRoot 'Set-RevitMcpTarget.ps1'
if (-not (Test-Path $targetScript)) {
    throw "tools\Set-RevitMcpTarget.ps1 is missing; the installer invokes it to register the MCP server."
}
New-Item -ItemType Directory -Path (Join-Path $stage 'tools') -Force | Out-Null
Copy-Item $targetScript (Join-Path $stage 'tools') -Force

$serverFiles = @(Get-ChildItem $serverOut -Recurse -File)
$serverMb = [math]::Round((($serverFiles | Measure-Object Length -Sum).Sum) / 1MB, 1)
Ok "MCP server  -  $($serverFiles.Count) files, $serverMb MB, 0 native modules"
# -------------------------------------------------------------------- read me
$readme = @"
mcp-servers-for-revit $Version
$('=' * (25 + $Version.Length))

Connect AI assistants to Autodesk Revit over the Model Context Protocol.

WHAT IS IN HERE
    One folder per Revit version. Use the one matching your Revit.

INSTALL (manual)
    1. Copy the CONTENTS of Revit<year>\ into:
           %AppData%\Autodesk\Revit\Addins\<year>\
       You should end up with:
           Addins\<year>\mcp-servers-for-revit.addin
           Addins\<year>\revit_mcp_plugin\...

    2. IF YOU DOWNLOADED THIS AS A ZIP, UNBLOCK IT FIRST.
       Windows marks files from a downloaded archive, and the .NET loader then
       refuses the DLL with "FileLoadException ... HRESULT 0x80131515", which
       Revit reports as "cannot run the external application".

       Right-click the ZIP > Properties > tick Unblock > OK, THEN extract.
       Unblocking after extraction does not clear the mark on the files inside.

       Already extracted? Run this in PowerShell:
           Get-ChildItem "`$env:AppData\Autodesk\Revit\Addins" -Recurse -Include *.dll,*.addin | Unblock-File

    3. Start Revit. If it asks about an unknown add-in, choose Always Load.

    4. Add-Ins ribbon > Revit MCP Switch to start the server, and Settings to
       choose which commands are enabled.

    Or use the Setup.exe, which does all of the above and needs no unblocking.

MCP SERVER
    The Revit side is only half of it. Your AI client has to be pointed at the
    MCP server as well.

    The Setup.exe does this for you - tick "Register the MCP server" during
    installation, with your AI client CLOSED. If you are installing from this
    ZIP instead, register it yourself:

        claude mcp add --scope user mcp-server-for-revit -- cmd /c npx -y mcp-server-for-revit

    --scope user matters: the default scope is "local", which registers the
    server only for the directory you ran the command in.

    Full instructions: https://github.com/mcp-servers-for-revit/mcp-servers-for-revit

REQUIREMENTS
    Windows, Autodesk Revit 2020-2027, and Node.js 18+ for the MCP server.
"@

Set-Content -Path (Join-Path $stage 'READ ME FIRST.txt') -Value $readme -Encoding UTF8

# ----------------------------------------------------------------------- zip
if (-not $NoZip) {
    Step 'Compressing'
    foreach ($row in $summary) {
        $src = Join-Path $stage "Revit$($row.Year)"
        $zip = Join-Path $repo "dist\mcp-servers-for-revit-v$Version-Revit$($row.Year).zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }

        # Zip the CONTENTS, not the folder, so extracting into the Addins folder
        # produces the documented layout rather than a nested Revit2027\ level.
        Compress-Archive -Path (Join-Path $src '*') -DestinationPath $zip -CompressionLevel Optimal
        Ok "$(Split-Path $zip -Leaf)  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
    }
}

Write-Host ''
$summary | Format-Table Year, Config, Files, SizeMB, DupMB, PdbsCut -AutoSize
$totalMb = [math]::Round((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Staged $($summary.Count) Revit version(s), $totalMb MB total, at:" -ForegroundColor Green
Write-Host "  $stage" -ForegroundColor Green
Write-Host ''
Note 'Next: tools\Make-Installer.ps1 turns this into a single Setup.exe.'
