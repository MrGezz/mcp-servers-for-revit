# Harness for Set-RevitMcpTarget.ps1.
# Every guard is aimed at known-bad input first, so a green result means the
# check can actually fail rather than being unable to.
#
#   pwsh -File tools\Set-RevitMcpTarget.selftest.ps1
#
# Case [2] can only PROVE the running-application guard while Claude Desktop is
# actually up; it reports rather than passes vacuously when it is closed.
param(
    [string]$Script,
    [string]$RepoRoot,
    [string]$Sandbox
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Script))   { $Script   = Join-Path $PSScriptRoot 'Set-RevitMcpTarget.ps1' }
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($Sandbox))  { $Sandbox  = Join-Path ([System.IO.Path]::GetTempPath()) 'revit-mcp-target-selftest' }

if (-not (Test-Path -LiteralPath $Script -PathType Leaf)) { throw "script under test not found: $Script" }
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
Write-Host "script  : $Script"
Write-Host "repo    : $RepoRoot"
Write-Host "sandbox : $Sandbox"
$pass = 0; $fail = 0; $skip = 0

# Write UTF-8 WITHOUT a BOM, on Windows PowerShell 5.1 as well as PowerShell 7.
#
# Set-Content's 'utf8NoBOM' encoding value is PowerShell 7 ONLY: on 5.1 it is not
# in the encoding enum and the call throws. This script is invoked by the
# installer through powershell.exe -- 5.1 is the one shell present on every
# supported Windows -- so a 7-only call here is a script that cannot run where
# it is needed most. The .NET overload below behaves identically on both.
#
# No BOM matters: these are JSON config files owned by other applications, and a
# leading BOM makes a strict JSON parser reject the file.
function Write-Utf8NoBom {
    param([string] $Path, [string] $Text)
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Check([string]$name, [scriptblock]$body) {
    try {
        $r = & $body
        if ($r -eq $true) { Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
        elseif ($r -is [string] -and $r.StartsWith('SKIP:')) {
            # A check that cannot fail is not evidence. Say so rather than
            # counting it green.
            Write-Host "  SKIP  $name  -> $($r.Substring(5).Trim())" -ForegroundColor Yellow
            $script:skip++
        }
        else { Write-Host "  FAIL  $name  -> $r" -ForegroundColor Red; $script:fail++ }
    } catch {
        Write-Host "  FAIL  $name  -> EXCEPTION $($_.Exception.Message)" -ForegroundColor Red
        $script:fail++
    }
}

if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $Sandbox | Out-Null

$BuildEntry = Join-Path $RepoRoot 'server\build\index.js'

function New-Config([string]$name, [bool]$withEntry = $true, [bool]$withServers = $true) {
    $p = Join-Path $Sandbox $name
    if ($withServers) {
        $servers = [ordered]@{
            'other-server' = [ordered]@{ command = 'node'; args = @('C:\some\other.mjs'); env = [ordered]@{ FLAG = '1' } }
        }
        if ($withEntry) {
            $servers['mcp-server-for-revit'] = [ordered]@{ command = 'cmd'; args = @('/c','npx','-y','mcp-server-for-revit') }
        }
        $servers['third'] = [ordered]@{ command = 'node'; args = @('C:\third.mjs') }
        $doc = [ordered]@{ someTopLevelKey = 'preserve-me'; mcpServers = $servers }
    } else {
        $doc = [ordered]@{ someTopLevelKey = 'preserve-me' }
    }
    Write-Utf8NoBom -Path $p -Text ($doc | ConvertTo-Json -Depth 100)
    return $p
}

function Run([string[]]$ArgList, [hashtable]$EnvVars = @{}) {
    $old = @{}
    foreach ($k in $EnvVars.Keys) { $old[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $EnvVars[$k]) }
    try {
        $out = & pwsh -NoProfile -File $Script @ArgList 2>&1 | Out-String
        return [pscustomobject]@{ Code = $LASTEXITCODE; Out = $out }
    } finally {
        foreach ($k in $old.Keys) { [Environment]::SetEnvironmentVariable($k, $old[$k]) }
    }
}

function Get-Entry([string]$path) {
    (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).mcpServers.'mcp-server-for-revit'
}

Write-Host "`n== classifier ==" -ForegroundColor Cyan

function Test-DesktopUp {
    $rows = @(Get-CimInstance Win32_Process -Filter "Name='claude.exe'" -ErrorAction SilentlyContinue)
    return @($rows | Where-Object { $_.ExecutablePath -match 'WindowsApps\\Claude_' -or $_.ExecutablePath -match '\\Claude\\app\\Claude\.exe$' }).Count -gt 0
}

Check '[1] Claude Code and Claude Desktop are distinguished by path' {
    $rows = @(Get-CimInstance Win32_Process -Filter "Name='claude.exe'" -ErrorAction SilentlyContinue)
    $code    = @($rows | Where-Object { $_.ExecutablePath -match 'claude-code' })
    $desktop = @($rows | Where-Object { $_.ExecutablePath -match 'WindowsApps\\Claude_' })
    if ($code.Count -eq 0)    { return 'SKIP: no claude-code process running; discrimination unproven' }
    if ($desktop.Count -eq 0) { return 'SKIP: no Claude Desktop process running; discrimination unproven' }
    return $true
}

Write-Host "`n== guard (red path) ==" -ForegroundColor Cyan

Check '[2] refuses to touch the LIVE config while Claude Desktop is running (exit 3)' {
    if (-not (Test-DesktopUp)) { return 'SKIP: Claude Desktop is closed, so the guard cannot be observed firing' }
    $c = New-Config 'live-seam.json'
    $r = Run @('-RepoRoot', $RepoRoot, '-Apply') @{ ICZ_CLAUDE_CONFIG = $c }
    if ($r.Code -ne 3) { return "expected exit 3, got $($r.Code)" }
    if ($r.Out -notmatch 'Claude Desktop is running') { return 'no explanation in output' }
    $e = Get-Entry $c
    if ($e.command -ne 'cmd') { return 'REFUSED BUT STILL WROTE - guard is decoration' }
    return $true
}

Check '[3] the guard did NOT fire merely because Claude Code is running' {
    $c = New-Config 'code-not-blocker.json'
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Code -ne 0) { return "expected exit 0 on sandbox path, got $($r.Code)" }
    return $true
}

Write-Host "`n== green path ==" -ForegroundColor Cyan

Check '[4] dry run reports a change and writes NOTHING' {
    $c = New-Config 'dry.json'
    $before = Get-Content -LiteralPath $c -Raw
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c)
    if ($r.Code -ne 0) { return "exit $($r.Code)" }
    if ($r.Out -notmatch 'DRY RUN') { return 'no DRY RUN notice' }
    if ((Get-Content -LiteralPath $c -Raw) -ne $before) { return 'FILE WAS MODIFIED during a dry run' }
    return $true
}

Check '[5] -Apply repoints the entry at this repo build' {
    $c = New-Config 'apply.json'
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Code -ne 0) { return "exit $($r.Code): $($r.Out)" }
    $e = Get-Entry $c
    if ($e.command -ne 'node') { return "command=$($e.command)" }
    if ($e.args[0] -ne $BuildEntry) { return "args[0]=$($e.args[0])" }
    return $true
}

Check '[6] other server entries and top-level keys survive' {
    $c = New-Config 'collateral.json'
    Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply') | Out-Null
    $j = Get-Content -LiteralPath $c -Raw | ConvertFrom-Json
    if ($j.someTopLevelKey -ne 'preserve-me') { return 'top-level key lost' }
    if ($j.mcpServers.'other-server'.args[0] -ne 'C:\some\other.mjs') { return 'other-server altered' }
    if ($j.mcpServers.'other-server'.env.FLAG -ne '1') { return 'other-server env lost' }
    if ($j.mcpServers.'third'.command -ne 'node') { return 'third entry lost' }
    return $true
}

Check '[7] a second -Apply is idempotent (already correct)' {
    $c = New-Config 'idem.json'
    Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply') | Out-Null
    $after1 = Get-Content -LiteralPath $c -Raw
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Out -notmatch 'already correct') { return 'did not report already-correct' }
    if ((Get-Content -LiteralPath $c -Raw) -ne $after1) { return 'file changed on idempotent run' }
    return $true
}

Check '[8] -Revert restores the published-package form' {
    $c = New-Config 'revert.json'
    Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply') | Out-Null
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply', '-Revert')
    if ($r.Code -ne 0) { return "exit $($r.Code)" }
    $e = Get-Entry $c
    if ($e.command -ne 'cmd') { return "command=$($e.command)" }
    if (($e.args -join ' ') -ne '/c npx -y mcp-server-for-revit') { return "args=$($e.args -join ' ')" }
    return $true
}

Check '[9] an existing env block on the entry is preserved' {
    $c = New-Config 'envkeep.json'
    $j = Get-Content -LiteralPath $c -Raw | ConvertFrom-Json
    $j.mcpServers.'mcp-server-for-revit' | Add-Member -NotePropertyName env -NotePropertyValue ([pscustomobject]@{ REVIT_MCP_PORT = '8080' })
    Write-Utf8NoBom -Path $c -Text ($j | ConvertTo-Json -Depth 100)
    Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply') | Out-Null
    $e = Get-Entry $c
    if ($e.env.REVIT_MCP_PORT -ne '8080') { return 'env block dropped' }
    if ($e.command -ne 'node') { return 'entry not repointed' }
    return $true
}

Write-Host "`n== red paths ==" -ForegroundColor Cyan

Check '[10] invalid JSON is reported, not silently rewritten' {
    $c = Join-Path $Sandbox 'broken.json'
    Write-Utf8NoBom -Path $c -Text '{ this is not json'
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Code -ne 1) { return "expected exit 1, got $($r.Code)" }
    if ((Get-Content -LiteralPath $c -Raw).Trim() -ne '{ this is not json') { return 'file was modified' }
    return $true
}

Check '[11] a config with no mcpServers section is skipped, not corrupted' {
    $c = New-Config 'nomcp.json' $true $false
    $before = Get-Content -LiteralPath $c -Raw
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Code -ne 0) { return "exit $($r.Code)" }
    if ($r.Out -notmatch "no 'mcpServers' section") { return 'no skip notice' }
    if ((Get-Content -LiteralPath $c -Raw) -ne $before) { return 'file modified' }
    return $true
}

Check '[12] a missing entry is skipped without -AddIfMissing' {
    $c = New-Config 'noentry.json' $false
    $before = Get-Content -LiteralPath $c -Raw
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Code -ne 0) { return "exit $($r.Code)" }
    if ((Get-Content -LiteralPath $c -Raw) -ne $before) { return 'file modified without -AddIfMissing' }
    return $true
}

Check '[13] -AddIfMissing creates the entry' {
    $c = New-Config 'add.json' $false
    $r = Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply', '-AddIfMissing')
    if ($r.Code -ne 0) { return "exit $($r.Code): $($r.Out)" }
    $e = Get-Entry $c
    if ($null -eq $e) { return 'entry not created' }
    if ($e.command -ne 'node') { return "command=$($e.command)" }
    return $true
}

Check '[14] a missing build refuses rather than writing a dead path (exit 2)' {
    $c = New-Config 'nobuild.json'
    $fakeRoot = Join-Path $Sandbox 'fakerepo'
    New-Item -ItemType Directory -Path (Join-Path $fakeRoot 'server') -Force | Out-Null
    $before = Get-Content -LiteralPath $c -Raw
    $r = Run @('-RepoRoot', $fakeRoot, '-ConfigPath', $c, '-Apply')
    if ($r.Code -ne 2) { return "expected exit 2, got $($r.Code)" }
    if ((Get-Content -LiteralPath $c -Raw) -ne $before) { return 'file modified despite missing build' }
    return $true
}

Check '[15] a backup is written beside the config on a real change' {
    $c = New-Config 'backup.json'
    Run @('-RepoRoot', $RepoRoot, '-ConfigPath', $c, '-Apply') | Out-Null
    $baks = @(Get-ChildItem -LiteralPath $Sandbox -Filter 'backup.json.*.pre.bak')
    if ($baks.Count -lt 1) { return 'no backup written' }
    $b = Get-Content -LiteralPath $baks[0].FullName -Raw | ConvertFrom-Json
    if ($b.mcpServers.'mcp-server-for-revit'.command -ne 'cmd') { return 'backup does not hold the pre-image' }
    return $true
}

Write-Host ""
Write-Host "$pass passed, $fail failed, $skip skipped" -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
if ($skip -gt 0) {
    Write-Host "$skip check(s) could not be observed on this machine - see SKIP lines above." -ForegroundColor Yellow
}
if ($fail -eq 0) { Write-Host 'ALL PASS' -ForegroundColor Green }
exit $(if ($fail -eq 0) { 0 } else { 1 })
