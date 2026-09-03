param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# --- confirm ---
# This fork releases from features/icz-addin, not main, so the script works on
# whatever branch is checked out and refuses a dirty tree instead of hard-
# resetting it (the previous version checked out main and discarded everything).
Push-Location $root
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
$dirty  = @(git status --porcelain)
Pop-Location
if ($dirty.Count -gt 0) {
    Write-Host "The working tree has uncommitted changes. Commit or stash them first." -ForegroundColor Red
    exit 1
}
Write-Host "This will bump the version to $Version on branch '$branch', commit, and tag v$Version." -ForegroundColor Yellow
$answer = Read-Host "Continue? (y/N)"
if ($answer -ne 'y') {
    Write-Host "Aborted."
    exit 0
}

# --- bring the branch up to date (fast-forward only) ---
Push-Location $root
git pull --ff-only
Pop-Location

# --- server/package.json ---
$pkg = Get-Content "$root/server/package.json" -Raw | ConvertFrom-Json
$pkg.version = $Version
$pkg | ConvertTo-Json -Depth 10 | Set-Content "$root/server/package.json" -NoNewline
Write-Host "server/package.json -> $Version"

# --- server/package-lock.json ---
Push-Location "$root/server"
npm install --package-lock-only --silent 2>$null
Pop-Location
Write-Host "server/package-lock.json -> $Version"

# --- plugin/Properties/AssemblyInfo.cs ---
$assemblyInfo = "$root/plugin/Properties/AssemblyInfo.cs"
$fourPart = "$Version.0"
(Get-Content $assemblyInfo -Raw) `
    -replace 'AssemblyVersion\("[^"]+"\)',    "AssemblyVersion(`"$fourPart`")" `
    -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$fourPart`")" |
    Set-Content $assemblyInfo -NoNewline
Write-Host "plugin/Properties/AssemblyInfo.cs -> $fourPart"

# --- commandset/RevitMCPCommandSet.csproj ---
# The command set has no AssemblyInfo.cs; its version resource comes from the
# <Version> property in the project file.
$csproj = "$root/commandset/RevitMCPCommandSet.csproj"
(Get-Content $csproj -Raw) `
    -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>" |
    Set-Content $csproj -NoNewline
Write-Host "commandset/RevitMCPCommandSet.csproj -> $Version"

# --- git commit + tag ---
Push-Location $root
git add server/package.json server/package-lock.json plugin/Properties/AssemblyInfo.cs commandset/RevitMCPCommandSet.csproj
git commit -m "$Version"
git tag "v$Version"
Pop-Location

Write-Host ""
Write-Host "Done! Run 'git push origin $branch --tags' to trigger the release."
