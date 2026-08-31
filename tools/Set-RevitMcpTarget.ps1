<#
.SYNOPSIS
    Point the `mcp-server-for-revit` MCP entry at THIS repository's build instead
    of the published npm package.

.DESCRIPTION
    A config that launches the server with `npx -y mcp-server-for-revit` runs the
    PUBLISHED package, not this working tree. Everything built here - the Dynamo
    channel, the 127.0.0.1 fix, every server-side change - is then unreachable,
    and a "live test" silently exercises someone else's code. That is exactly what
    happened during the live bridge test on 2026-08-31; see
    docs/live-bridge-test-2026-08-31.md.

    Measured difference on that date: published build registered 26 tools, this
    repository's build registers 31. The five extra are the Dynamo tools.

    This script rewrites one entry:

        "mcp-server-for-revit": { "command": "cmd",  "args": ["/c","npx","-y","mcp-server-for-revit"] }
                             ->  { "command": "node", "args": ["<repo>\server\build\index.js"] }

    -Revert puts the npx form back.

.PARAMETER Apply
    Actually write. Without it the script only REPORTS what it would do - a dry
    run is the default because this file is owned by another application.

.PARAMETER Revert
    Restore the published-package (`npx -y`) form.

.PARAMETER Build
    Run `npm run build` in server/ before patching, so the target cannot be stale.

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of the folder holding this script.

.PARAMETER ConfigPath
    Explicit config file to operate on. Supplying this makes the run NON-LIVE and
    skips the running-application guard, so a harness can exercise this script
    against a sandbox copy. Never pass it to patch the real config while Claude
    Desktop is up.

.PARAMETER IncludeClaudeCode
    Also patch %USERPROFILE%\.claude\settings.json (Claude Code's own config).

.PARAMETER AddIfMissing
    Create the entry when a config has no `mcp-server-for-revit` key yet, instead
    of skipping that file.

.EXAMPLE
    pwsh -File tools\Set-RevitMcpTarget.ps1
    Report only: show every config found and what would change.

.EXAMPLE
    pwsh -File tools\Set-RevitMcpTarget.ps1 -Build -Apply
    Rebuild the server, then repoint the config. Claude Desktop must be closed.

.NOTES
    THE CONFIG IS APPLICATION-OWNED STATE. Claude Desktop rewrites it on exit, so
    an edit made while it is running is silently discarded - the file will look
    correct for a moment and then revert. This script therefore REFUSES to touch a
    live config while the application is running. Real verification is the tool
    list after a relaunch, never what the file says a second after writing.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Revert,
    [switch]$Build,
    [string]$RepoRoot,
    [string]$ConfigPath,
    [switch]$IncludeClaudeCode,
    [switch]$AddIfMissing
)

$ErrorActionPreference = 'Stop'

$ENTRY_KEY   = 'mcp-server-for-revit'
$STAMP       = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:Fail = 0

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

function Write-Head([string]$Text) {
    Write-Host ''
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ('-' * $Text.Length) -ForegroundColor DarkGray
}
function Write-Ok   ([string]$m) { Write-Host "  OK    $m" -ForegroundColor Green }
function Write-Info ([string]$m) { Write-Host "  ..    $m" -ForegroundColor Gray }
function Write-Warn ([string]$m) { Write-Host "  WARN  $m" -ForegroundColor Yellow }
function Write-Bad  ([string]$m) { Write-Host "  FAIL  $m" -ForegroundColor Red; $script:Fail++ }

# --- repository and build target ---------------------------------------------

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot   = (Resolve-Path -LiteralPath $RepoRoot).Path
$ServerDir  = Join-Path $RepoRoot 'server'
$BuildEntry = Join-Path $ServerDir 'build\index.js'

Write-Head "Repository"
Write-Info "root   : $RepoRoot"
Write-Info "server : $ServerDir"

if (-not (Test-Path -LiteralPath $ServerDir -PathType Container)) {
    Write-Bad "server/ not found - is -RepoRoot correct?"
    exit 2
}

if ($Build -and -not $Revert) {
    Write-Head "Build"
    if (-not (Test-Path -LiteralPath (Join-Path $ServerDir 'node_modules') -PathType Container)) {
        Write-Info "node_modules missing - running npm install"
        Push-Location $ServerDir
        try { & npm install } finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { Write-Bad "npm install failed ($LASTEXITCODE)"; exit 2 }
    }
    Write-Info "npm run build"
    Push-Location $ServerDir
    try { & npm run build } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Write-Bad "npm run build failed ($LASTEXITCODE)"; exit 2 }
    Write-Ok "build completed"
}

if (-not $Revert) {
    if (-not (Test-Path -LiteralPath $BuildEntry -PathType Leaf)) {
        Write-Bad "build entry missing: $BuildEntry"
        Write-Info "re-run with -Build, or run 'npm run build' in server/ first"
        exit 2
    }

    # A build older than its sources is the same failure as no build at all: the
    # config would point at a real file that does not contain the current work.
    $newestSrc = Get-ChildItem -LiteralPath (Join-Path $ServerDir 'src') -Recurse -File -Filter *.ts -ErrorAction SilentlyContinue |
                 Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $newestBuild = Get-ChildItem -LiteralPath (Join-Path $ServerDir 'build') -Recurse -File -Filter *.js -ErrorAction SilentlyContinue |
                   Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newestSrc -and $newestBuild) {
        if ($newestBuild.LastWriteTimeUtc -lt $newestSrc.LastWriteTimeUtc) {
            Write-Warn ("build is STALE - newest source $($newestSrc.Name) is newer than newest build $($newestBuild.Name). Re-run with -Build.")
        } else {
            Write-Ok ("build is fresh (newest build $($newestBuild.LastWriteTimeUtc.ToString('u')))")
        }
    }

    $toolCount = 0
    Get-ChildItem -LiteralPath (Join-Path $ServerDir 'build') -Recurse -File -Filter *.js -ErrorAction SilentlyContinue |
        ForEach-Object {
            $t = Get-Content -LiteralPath $_.FullName -Raw
            $toolCount += ([regex]::Matches($t, 'server\.tool\(\s*["''][a-z_]+["'']')).Count
        }
    Write-Ok "build entry present, $toolCount tool registration(s) found"
}

# --- desired shape ------------------------------------------------------------

if ($Revert) {
    $desired = [ordered]@{ command = 'cmd'; args = @('/c', 'npx', '-y', 'mcp-server-for-revit') }
    $desiredLabel = 'npx -y mcp-server-for-revit  (published package)'
} else {
    $desired = [ordered]@{ command = 'node'; args = @($BuildEntry) }
    $desiredLabel = "node $BuildEntry  (this repository)"
}
Write-Head "Target"
Write-Info $desiredLabel

# --- locate configs -----------------------------------------------------------

function Get-DesktopConfigCandidates {
    if (-not [string]::IsNullOrWhiteSpace($env:ICZ_CLAUDE_CONFIG)) { return @($env:ICZ_CLAUDE_CONFIG) }
    return @(
        (Join-Path $env:LOCALAPPDATA 'Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json'),
        (Join-Path $env:APPDATA      'Claude\claude_desktop_config.json')
    )
}

$isLive = [string]::IsNullOrWhiteSpace($ConfigPath)
$targets = [System.Collections.Generic.List[object]]::new()

if ($isLive) {
    foreach ($c in Get-DesktopConfigCandidates) {
        if (Test-Path -LiteralPath $c -PathType Leaf) {
            $targets.Add([pscustomobject]@{ Path = $c; Kind = 'Claude Desktop' })
        }
    }
    if ($IncludeClaudeCode) {
        $cc = Join-Path $env:USERPROFILE '.claude\settings.json'
        if (Test-Path -LiteralPath $cc -PathType Leaf) {
            $targets.Add([pscustomobject]@{ Path = $cc; Kind = 'Claude Code' })
        }
    }
} else {
    $targets.Add([pscustomobject]@{ Path = $ConfigPath; Kind = 'explicit' })
}

Write-Head "Configuration files"
if ($targets.Count -eq 0) {
    Write-Bad "no configuration file found"
    exit 2
}
foreach ($t in $targets) { Write-Info "$($t.Kind.PadRight(15)) $($t.Path)" }

# --- running-application guard ------------------------------------------------
#
# Only on the LIVE path. Passing -ConfigPath means a sandbox copy, where the
# guard would only prevent the harness from testing anything.
#
# CLASSIFY BY PATH, NOT BY NAME. Claude Code ships its own claude.exe
# (...\AppData\Roaming\Claude\claude-code\<ver>\claude.exe), so a name-only match
# fires whenever Claude Code is open - which is precisely when someone would run
# this script, making the tool refuse itself forever. Only Claude DESKTOP owns
# claude_desktop_config.json.

function Get-ClaudeDesktopProcess {
    $rows = @(Get-CimInstance Win32_Process -Filter "Name='claude.exe'" -ErrorAction SilentlyContinue)
    $desktop = @()
    $code    = @()
    $unknown = @()
    foreach ($r in $rows) {
        $p = $r.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($p)) { $unknown += $r; continue }
        if ($p -match 'claude-code') { $code += $r; continue }
        if ($p -match 'WindowsApps\\Claude_' -or $p -match '\\Claude\\app\\Claude\.exe$' -or $p -match '\\AnthropicClaude\\') {
            $desktop += $r; continue
        }
        $unknown += $r
    }
    return [pscustomobject]@{ Desktop = $desktop; Code = $code; Unknown = $unknown }
}

if ($isLive -and $Apply) {
    Write-Head "Running-application guard"
    $procs = Get-ClaudeDesktopProcess
    if ($procs.Code.Count -gt 0) {
        Write-Info "Claude Code is running ($($procs.Code.Count) process(es)) - not a blocker, it does not own this file"
    }
    if ($procs.Unknown.Count -gt 0) {
        Write-Warn "$($procs.Unknown.Count) claude.exe process(es) could not be classified by path - not treated as Desktop"
    }
    $running = $procs.Desktop
    if ($running.Count -gt 0) {
        Write-Bad "Claude Desktop is running (PID $(($running.ProcessId) -join ', '))"
        Write-Host ''
        Write-Host '  This configuration file is owned by the application. It rewrites the' -ForegroundColor Yellow
        Write-Host '  file on exit, so an edit made now is silently discarded - the change' -ForegroundColor Yellow
        Write-Host '  appears to land and then reverts.' -ForegroundColor Yellow
        Write-Host ''
        Write-Host '  Close Claude Desktop completely, then re-run with -Apply.' -ForegroundColor Yellow
        exit 3
    }
    Write-Ok "Claude Desktop is not running"
}

# --- patch --------------------------------------------------------------------

function Get-EntryMap([object]$Servers) {
    $m = @{}
    if ($null -eq $Servers) { return $m }
    foreach ($p in $Servers.PSObject.Properties) { $m[$p.Name] = ($p.Value | ConvertTo-Json -Depth 100 -Compress) }
    return $m
}

$changed = 0
$skipped = 0

foreach ($t in $targets) {
    Write-Head "Patch: $($t.Path)"

    $rawBefore = Get-Content -LiteralPath $t.Path -Raw
    try { $json = $rawBefore | ConvertFrom-Json } catch { Write-Bad "not valid JSON: $($_.Exception.Message)"; continue }

    if ($null -eq $json.mcpServers) {
        Write-Warn "no 'mcpServers' section - skipped"
        $skipped++
        continue
    }

    $beforeMap = Get-EntryMap $json.mcpServers
    $hasEntry  = $json.mcpServers.PSObject.Properties.Name -contains $ENTRY_KEY

    if (-not $hasEntry -and -not $AddIfMissing) {
        Write-Info "no '$ENTRY_KEY' entry here - skipped (use -AddIfMissing to create it)"
        $skipped++
        continue
    }

    if ($hasEntry) {
        Write-Info ("current : " + ($json.mcpServers.$ENTRY_KEY | ConvertTo-Json -Depth 100 -Compress))
    } else {
        Write-Info "current : (absent - will be created)"
    }

    # Preserve any env block the user set on this entry.
    $newEntry = [ordered]@{ command = $desired.command; args = $desired.args }
    if ($hasEntry -and $json.mcpServers.$ENTRY_KEY.PSObject.Properties.Name -contains 'env') {
        $newEntry['env'] = $json.mcpServers.$ENTRY_KEY.env
        Write-Info "preserving existing 'env' block on this entry"
    }
    $newEntryJson = ($newEntry | ConvertTo-Json -Depth 100 -Compress)
    Write-Info ("desired : " + $newEntryJson)

    if ($hasEntry -and $beforeMap[$ENTRY_KEY] -eq $newEntryJson) {
        Write-Ok "already correct - nothing to do"
        continue
    }

    if (-not $Apply) {
        Write-Warn "DRY RUN - would rewrite this entry. Re-run with -Apply to write."
        $changed++
        continue
    }

    $backup = "$($t.Path).$STAMP.pre.bak"
    Copy-Item -LiteralPath $t.Path -Destination $backup
    Write-Info "backup  : $backup"

    if ($hasEntry) {
        $json.mcpServers.$ENTRY_KEY = [pscustomobject]$newEntry
    } else {
        $json.mcpServers | Add-Member -NotePropertyName $ENTRY_KEY -NotePropertyValue ([pscustomobject]$newEntry)
    }

    Write-Utf8NoBom -Path $t.Path -Text ($json | ConvertTo-Json -Depth 100)

    # --- verify by re-reading, not by assuming the write worked ---------------
    $rawAfter = Get-Content -LiteralPath $t.Path -Raw
    try { $check = $rawAfter | ConvertFrom-Json } catch {
        Copy-Item -LiteralPath $backup -Destination $t.Path -Force
        Write-Bad "wrote invalid JSON - backup restored, file unchanged"
        continue
    }

    $afterMap = Get-EntryMap $check.mcpServers

    if ($afterMap[$ENTRY_KEY] -ne $newEntryJson) {
        Copy-Item -LiteralPath $backup -Destination $t.Path -Force
        Write-Bad "entry did not land as intended - backup restored"
        continue
    }

    # Every OTHER server must be untouched. Serialising the whole document can
    # reorder or reshape things; this is the check that would catch it.
    $collateral = @()
    foreach ($k in $beforeMap.Keys) {
        if ($k -eq $ENTRY_KEY) { continue }
        if (-not $afterMap.ContainsKey($k)) { $collateral += "$k (lost)"; continue }
        if ($afterMap[$k] -ne $beforeMap[$k]) { $collateral += "$k (altered)" }
    }
    foreach ($k in $afterMap.Keys) {
        if ($k -ne $ENTRY_KEY -and -not $beforeMap.ContainsKey($k)) { $collateral += "$k (unexpectedly added)" }
    }
    if ($collateral.Count -gt 0) {
        Copy-Item -LiteralPath $backup -Destination $t.Path -Force
        Write-Bad ("collateral damage to other entries - backup restored: " + ($collateral -join ', '))
        continue
    }

    $otherTop = @($check.PSObject.Properties.Name | Where-Object { $_ -ne 'mcpServers' })
    $beforeTop = @(($rawBefore | ConvertFrom-Json).PSObject.Properties.Name | Where-Object { $_ -ne 'mcpServers' })
    $lostTop = @($beforeTop | Where-Object { $otherTop -notcontains $_ })
    if ($lostTop.Count -gt 0) {
        Copy-Item -LiteralPath $backup -Destination $t.Path -Force
        Write-Bad ("would lose top-level key(s): " + ($lostTop -join ', ') + " - backup restored")
        continue
    }

    Write-Ok ("patched and verified - $($beforeMap.Count) server entr(ies) intact, " +
              "$($otherTop.Count) other top-level key(s) preserved")
    $changed++
}

# --- summary ------------------------------------------------------------------

Write-Head "Summary"
Write-Info "configs examined : $($targets.Count)"
Write-Info "changed          : $changed"
Write-Info "skipped          : $skipped"

if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host "FAILED ($script:Fail)" -ForegroundColor Red
    exit 1
}

if (-not $Apply -and $changed -gt 0) {
    Write-Host ''
    Write-Host 'Dry run only. Re-run with -Apply (Claude Desktop closed) to write.' -ForegroundColor Yellow
    exit 0
}

if ($Apply -and $changed -gt 0) {
    Write-Host ''
    Write-Host 'Restart Claude Desktop, then confirm by the TOOL LIST, not the file:' -ForegroundColor Cyan
    if ($Revert) {
        Write-Host '  expect 26 tools and no dynamo_* tools (published package).' -ForegroundColor Cyan
    } else {
        Write-Host '  expect dynamo_status / dynamo_list_graphs / dynamo_read_graph /' -ForegroundColor Cyan
        Write-Host '  dynamo_edit_graph / dynamo_run_graph to be present.' -ForegroundColor Cyan
        Write-Host '  If they are absent, the edit did not survive - the application was up.' -ForegroundColor Cyan
    }
}

exit 0
