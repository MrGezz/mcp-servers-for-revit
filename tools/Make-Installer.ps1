<#
    Make-Installer.ps1 - build mcp-servers-for-revit-<version>-Setup.exe.

    Package.ps1 produces per-version folders and ZIPs; this turns them into one
    installer with a wizard.

    WHY, GIVEN THE ZIPS ALREADY EXIST. Four of the open issues on this repository
    are installation failures rather than code failures, and the installer closes
    or checks every one of them:

      #12  Windows blocked the DLLs because they came from a downloaded ZIP.
           Files written by a setup program carry no Zone.Identifier stream,
           so there is nothing to unblock. This one goes away outright.
      #12  two .addin files sharing a ClientId. The [Code] section deletes the
           known orphan before installing.
      #47  the tree copied to the wrong level. The installer picks the path.
      #1   the MCP server half never registered. The final page says so, with
           the exact command.

    NEEDS INNO SETUP:
        winget install JRSoftware.InnoSetup

    SIGNING. The output is UNSIGNED, so SmartScreen warns on any machine that has
    not seen it: "Windows protected your PC" -> More info -> Run anyway. No
    packaging tool fixes that; it takes an Authenticode certificate.

    Usage:
        powershell -ExecutionPolicy Bypass -File .\tools\Make-Installer.ps1
        powershell -ExecutionPolicy Bypass -File .\tools\Make-Installer.ps1 -SkipPackage
        powershell -ExecutionPolicy Bypass -File .\tools\Make-Installer.ps1 -Years 2026,2027
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [int[]]  $Years,
    [string] $Version,
    [switch] $SkipPackage,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Step { param([string] $T) Write-Host "==> $T" -ForegroundColor Cyan }
function Ok   { param([string] $T) Write-Host "    $T" -ForegroundColor Green }
function Note { param([string] $T) Write-Host "    $T" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------- ISCC
# winget installs Inno per-user by default, so check LOCALAPPDATA before the
# Program Files locations an administrator install would use.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup's compiler (ISCC.exe) was not found. Install it with 'winget install JRSoftware.InnoSetup', or ship the ZIPs from tools\Package.ps1 instead."
}

if (-not $Version) {
    $pkg = Join-Path $repo 'server\package.json'
    if (-not (Test-Path $pkg)) { throw "Cannot determine the version: $pkg is missing. Pass -Version." }
    $Version = (Get-Content $pkg -Raw | ConvertFrom-Json).version
}

$payload = Join-Path $repo "dist\mcp-servers-for-revit $Version"

Write-Host ''
Write-Host "mcp-servers-for-revit - build Setup.exe $Version" -ForegroundColor Cyan
Write-Host '--------------------------------------------------' -ForegroundColor Cyan
Ok "ISCC: $iscc"

# ------------------------------------------------------------------- payload
if (-not $SkipPackage) {
    Step 'Staging the payload'
    $pkgArgs = @('-Configuration', $Configuration, '-Version', $Version, '-NoZip')
    if ($Years)     { $pkgArgs += @('-Years', ($Years -join ',')) }
    if ($SkipBuild) { $pkgArgs += '-SkipBuild' }
    & (Join-Path $PSScriptRoot 'Package.ps1') @pkgArgs | Out-Null
    if ($LASTEXITCODE) { throw "Package.ps1 failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path $payload)) {
    throw "No payload at '$payload'. Run tools\Package.ps1 first, or drop -SkipPackage."
}

# The .iss declares a [Files] line per Revit year and ISCC fails hard on a
# missing source. Confirm every year the script expects is actually staged,
# here, where the error can name the fix.
$expected = @(2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027)
$missing  = $expected | Where-Object { -not (Test-Path (Join-Path $payload "Revit$_")) }
if ($missing) {
    throw ("The payload is missing Revit $($missing -join ', '). " +
           "The installer script expects all eight. Re-run Package.ps1 without -Years, " +
           "or edit tools\mcp-servers-for-revit.iss to drop those [Files] and [Components] lines.")
}
Ok "payload: $payload"

$payloadMb = [math]::Round((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Note "$payloadMb MB across $($expected.Count) Revit versions"

# ------------------------------------------------------------------- compile
Step 'Compiling with Inno Setup'
Note 'lzma2/max over ~285 MB - this takes a couple of minutes.'

$iss = Join-Path $PSScriptRoot 'mcp-servers-for-revit.iss'
$out = Join-Path $repo 'dist'

# ISCC reports compile errors on stderr. Under ErrorActionPreference=Stop,
# PowerShell turns native stderr into a terminating NativeCommandError, which
# killed this script BEFORE its own exit-code check and still returned 0 - a
# failed compile that looked like a successful build. Twice.
#
# So the preference is relaxed around the native call only, and the verdict is
# taken from the exit code, which is the thing ISCC actually sets.
$isccArgs = @(
    "/DAppVersion=$Version",
    "/DPayloadDir=$payload",
    "/DOutDir=$out",
    $iss
)

$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$log = & $iscc @isccArgs 2>&1 | ForEach-Object { "$_" }
$isccExit = $LASTEXITCODE
$ErrorActionPreference = $previousPreference

if ($isccExit -ne 0) {
    Write-Host ''
    $log | Where-Object { $_ -match 'Error' } | Select-Object -First 10 |
        ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "  ISCC failed (exit $isccExit)." -ForegroundColor Red
    exit 1
}

$setup = Join-Path $out "mcp-servers-for-revit-$Version-Setup.exe"
if (-not (Test-Path $setup)) { throw "ISCC reported success but '$setup' is missing." }

$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Ok "$setup  ($mb MB)"

Write-Host ''
Write-Host "Setup.exe built - $mb MB, single file, with a wizard." -ForegroundColor Green
Write-Host ''
Note 'It ticks the Revit versions it finds on the machine (by Revit.exe on disk,'
Note 'not the registry, which keeps keys for uninstalled versions), refuses to'
Note 'run while Revit is open, and clears the stale revit-mcp.addin from v1.0.0.'
Write-Host ''
Note 'UNSIGNED: SmartScreen shows "Windows protected your PC" on a machine that'
Note 'has not seen it. More info, then Run anyway.'

exit 0
