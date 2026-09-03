<#
.SYNOPSIS
    Copy this repository's builds to the two places a live test actually uses.

.DESCRIPTION
    A "live test" of this project runs whatever the AI client and Revit have
    INSTALLED, not what the working tree contains. Two copies matter:

        MCP server  %APPDATA%\mcp-servers-for-revit\server\build\   (Claude Desktop / Claude Code launch node on it)
        Revit add-in %APPDATA%\Autodesk\Revit\Addins\<year>\        (the plugin DLL + Commands\RevitMCPCommandSet\<year>\)

    This script refreshes both from the staged build outputs:

        server\build\                                   -> server copy (+ package.json; node_modules installed if absent)
        plugin\bin\AddIn <year> <Configuration> R<yy>\  -> add-in copy (PDBs stripped)

    The add-in copy is REFUSED while Revit is running: the DLLs are loaded and
    Windows will not let them be replaced, and a half-copied add-in is worse
    than a stale one. Close Revit, run again.

.PARAMETER Year
    Revit version to deploy (default 2026).

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER Build
    Run the TypeScript and C# builds first. C# builds pass -p:PublishAddinFiles=false
    so the Nice3point SDK does not publish a second copy of the command set into the
    Addins folder as a side effect.

.PARAMETER SkipServer / SkipAddin
    Deploy only the other half.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\Deploy-Local.ps1 -Build
    powershell -ExecutionPolicy Bypass -File .\tools\Deploy-Local.ps1 -SkipAddin
#>
[CmdletBinding()]
param(
    [int]    $Year = 2026,
    [string] $Configuration = 'Release',
    [switch] $Build,
    [switch] $SkipServer,
    [switch] $SkipAddin
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$yy   = $Year % 100

function Step { param([string] $T) Write-Host "==> $T" -ForegroundColor Cyan }
function Ok   { param([string] $T) Write-Host "    $T" -ForegroundColor Green }
function Warn { param([string] $T) Write-Host "    $T" -ForegroundColor Yellow }

$serverSrc  = Join-Path $repo 'server'
$serverDst  = Join-Path $env:APPDATA 'mcp-servers-for-revit\server'
$addinSrc   = Join-Path $repo "plugin\bin\AddIn $Year $Configuration R$yy"
$addinDst   = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"

if ($Build) {
    Step 'Build: MCP server (npm run build)'
    Push-Location $serverSrc
    try {
        if (-not (Test-Path 'node_modules')) { & npm install --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm install failed' } }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
    } finally { Pop-Location }

    if (-not $SkipAddin) {
        Step "Build: command set + plugin ($Configuration R$yy)"
        & dotnet build (Join-Path $repo 'commandset\RevitMCPCommandSet.csproj') -c "$Configuration R$yy" -p:PublishAddinFiles=false -nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'command set build failed' }
        & dotnet build (Join-Path $repo 'plugin\RevitMCPPlugin.csproj') -c "$Configuration R$yy" -nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }
    }
}

if (-not $SkipServer) {
    Step "Deploy: MCP server -> $serverDst"
    $buildDir = Join-Path $serverSrc 'build'
    if (-not (Test-Path (Join-Path $buildDir 'index.js'))) { throw "No server build at $buildDir. Run with -Build or 'npm run build' in server\ first." }

    New-Item -ItemType Directory -Force -Path $serverDst | Out-Null
    $dstBuild = Join-Path $serverDst 'build'
    if (Test-Path $dstBuild) { Remove-Item -Recurse -Force $dstBuild }
    Copy-Item -Recurse -Force $buildDir $dstBuild
    Copy-Item -Force (Join-Path $serverSrc 'package.json') (Join-Path $serverDst 'package.json')
    if (Test-Path (Join-Path $serverSrc 'package-lock.json')) { Copy-Item -Force (Join-Path $serverSrc 'package-lock.json') (Join-Path $serverDst 'package-lock.json') }

    if (-not (Test-Path (Join-Path $serverDst 'node_modules\@modelcontextprotocol'))) {
        Step 'node_modules missing in the deployed copy - installing runtime dependencies'
        Push-Location $serverDst
        try { & npm install --omit=dev --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm install in deployed copy failed' } }
        finally { Pop-Location }
    }
    $count = (Get-ChildItem -Recurse -File -Filter *.js $dstBuild).Count
    Ok "$count JS files deployed. Restart the AI client (Claude Desktop / Claude Code) to load them."
}

if (-not $SkipAddin) {
    Step "Deploy: Revit $Year add-in -> $addinDst"
    if (-not (Test-Path (Join-Path $addinSrc 'revit_mcp_plugin\RevitMCPPlugin.dll'))) {
        throw "No staged add-in at '$addinSrc'. Run with -Build, or build both projects with configuration '$Configuration R$yy'."
    }
    $revit = @(Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)
    if ($revit.Count -gt 0) {
        Warn "Revit is running (PID $(($revit | ForEach-Object { $_.Id }) -join ', ')). Its add-in DLLs are locked."
        Warn 'Close Revit and run this script again (use -SkipServer to redo only this half).'
        exit 3
    }
    New-Item -ItemType Directory -Force -Path $addinDst | Out-Null
    Copy-Item -Force (Join-Path $addinSrc 'mcp-servers-for-revit.addin') $addinDst
    $pluginDst = Join-Path $addinDst 'revit_mcp_plugin'
    Copy-Item -Recurse -Force (Join-Path $addinSrc 'revit_mcp_plugin\*') $pluginDst
    Get-ChildItem -Recurse -File -Filter *.pdb $pluginDst | Remove-Item -Force
    Ok "Add-in deployed. Start Revit; the MCP server auto-starts (log: $pluginDst\Logs)."
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
