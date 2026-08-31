<#
    Verify.ps1 - the whole gate in one command.

    Five checks, each chosen because it catches something the others cannot:

      1. YEARS    - the supported Revit versions are declared in SEVEN places
                    (two .csproj, the .sln, release.yml, Package.ps1, and the
                    .iss twice). Nothing makes them agree. A year added to the
                    csproj but not the .iss builds fine and then silently ships
                    an installer that cannot deploy it.

      2. BUILD    - every configuration compiles. A grep cannot tell you a file
                    parses; only a compiler can, and this project has already
                    had a marker audit pass on a .cs that did not.

      3. PAYLOAD  - the staged tree has the shape Revit needs. A missing
                    command.json or a second .addin produces no build error and
                    a dead add-in.

      4. SERVER   - the TypeScript compiles and the Dynamo harness passes,
                    including its round-trip fidelity checks against real graphs.

      5. INSTALLER- Check-InstallerScript.ps1 (Pascal Script shapes and install
                    destinations), then a real ISCC compile. The [Code] section
                    is not checked by anything else and THREE syntax errors in it
                    reached a build here; the destination check exists because
                    Revit 2027 stopped loading add-ins from %ProgramData%.

    Run it before landing anything.

        powershell -ExecutionPolicy Bypass -File .\tools\Verify.ps1
        powershell -ExecutionPolicy Bypass -File .\tools\Verify.ps1 -SkipBuild
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipBuild,
    [switch] $SkipServer,
    [string] $CorpusDir      # a folder of real .dyn graphs, for the Dynamo harness
)

$ErrorActionPreference = 'Stop'
$repo   = Split-Path $PSScriptRoot -Parent
$failed = @()
$passed = 0

function Section { param([string] $T) Write-Host ''; Write-Host "=========== $T ===========" -ForegroundColor Cyan }
function Pass { param([string] $T) $script:passed++; Write-Host "  PASS  $T" -ForegroundColor Green }
function Fail {
    param([string] $T, [string] $D = '')
    # The body was previously wrapped in an extra { }, which makes it a
    # scriptblock LITERAL: PowerShell emitted it to the output stream and never
    # ran it, so $failed was never appended to and this script could report ALL
    # PASS while checks were failing.
    $script:failed += $T
    Write-Host "  FAIL  $T" -ForegroundColor Red
    if ($D) { Write-Host "        $D" -ForegroundColor DarkRed }
}

# ============================================================== 1. YEARS
Section 'YEARS - the seven declarations must agree'

function YearsFrom([string] $Text, [string] $Pattern) {
    $found = [regex]::Matches($Text, $Pattern) | ForEach-Object { $_.Groups[1].Value }
    return ($found | Sort-Object -Unique)
}

$sources = @{}

$csproj = Get-Content (Join-Path $repo 'commandset\RevitMCPCommandSet.csproj') -Raw
$sources['commandset .csproj'] = YearsFrom $csproj 'Release R(\d\d)' | ForEach-Object { "20$_" }

$pproj = Get-Content (Join-Path $repo 'plugin\RevitMCPPlugin.csproj') -Raw
$sources['plugin .csproj'] = YearsFrom $pproj 'Release R(\d\d)' | ForEach-Object { "20$_" }

$sln = Get-Content (Join-Path $repo 'mcp-servers-for-revit.sln') -Raw
$sources['solution']  = YearsFrom $sln 'Release R(\d\d)\|Any CPU' | ForEach-Object { "20$_" }

$yml = Get-Content (Join-Path $repo '.github\workflows\release.yml') -Raw
$sources['release.yml'] = YearsFrom $yml 'Year = "(\d{4})"'

$pkg = Get-Content (Join-Path $PSScriptRoot 'Package.ps1') -Raw
if ($pkg -match '\$AllYears\s*=\s*@\(([^\)]*)\)') {
    $sources['Package.ps1'] = ($Matches[1] -split ',' | ForEach-Object { $_.Trim() } | Sort-Object -Unique)
}

$iss = Get-Content (Join-Path $PSScriptRoot 'mcp-servers-for-revit.iss') -Raw
$sources['.iss [Components]'] = YearsFrom $iss 'Name: "r(\d{4})"'
$sources['.iss [Files]']      = YearsFrom $iss 'PayloadDir\}\\Revit(\d{4})\\'

if ($iss -match 'FirstYear\s*=\s*(\d{4})' ) { $first = $Matches[1] }
if ($iss -match 'LastYear\s*=\s*(\d{4})'  ) { $last  = $Matches[1] }
if ($first -and $last) {
    $sources['.iss FirstYear..LastYear'] = @([int]$first..[int]$last | ForEach-Object { "$_" })
}

$reference = $sources['commandset .csproj']
Write-Host "  reference (commandset .csproj): $($reference -join ', ')" -ForegroundColor DarkGray

foreach ($name in ($sources.Keys | Sort-Object)) {
    $these = @($sources[$name])
    $diffA = $these   | Where-Object { $reference -notcontains $_ }
    $diffB = $reference | Where-Object { $these   -notcontains $_ }
    if ($diffA -or $diffB) {
        $d = @()
        if ($diffA) { $d += "extra: $($diffA -join ',')" }
        if ($diffB) { $d += "missing: $($diffB -join ',')" }
        Fail "$name agrees with the reference" ($d -join '  ')
    } else {
        Pass "$name  ($($these.Count) years)"
    }
}

# ============================================================== 2. BUILD
Section 'BUILD'
if ($SkipBuild) {
    Write-Host '  SKIPPED (-SkipBuild)' -ForegroundColor Yellow
} else {
    foreach ($year in $reference) {
        $cfg = "$Configuration R$($year.Substring(2))"
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $out = & dotnet build (Join-Path $repo 'mcp-servers-for-revit.sln') -c $cfg -v quiet --nologo -p:PublishAddinFiles=false 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prev

        $errors = @($out | Where-Object { $_ -match ': error ' })
        if ($code -ne 0 -or $errors) {
            Fail "$cfg compiles" (($errors | Select-Object -First 2) -join ' | ')
        } else {
            Pass "$cfg compiles"
        }
    }
}

# ============================================================== 3. PAYLOAD
Section 'PAYLOAD SHAPE'
$version = (Get-Content (Join-Path $repo 'server\package.json') -Raw | ConvertFrom-Json).version
$stage   = Join-Path $repo "dist\mcp-servers-for-revit $version"

if (-not (Test-Path $stage)) {
    Write-Host "  SKIPPED - nothing staged at '$stage'. Run tools\Package.ps1." -ForegroundColor Yellow
} else {
    foreach ($year in $reference) {
        $dir = Join-Path $stage "Revit$year"
        if (-not (Test-Path $dir)) { Fail "Revit $year is staged"; continue }

        $problems = @()
        foreach ($rel in @(
            'mcp-servers-for-revit.addin',
            'revit_mcp_plugin\RevitMCPPlugin.dll',
            'revit_mcp_plugin\Commands\RevitMCPCommandSet\command.json',
            "revit_mcp_plugin\Commands\RevitMCPCommandSet\$year\RevitMCPCommandSet.dll")) {
            if (-not (Test-Path (Join-Path $dir $rel))) { $problems += "missing $rel" }
        }

        $addins = @(Get-ChildItem $dir -Filter *.addin -File)
        if ($addins.Count -ne 1) { $problems += "$($addins.Count) .addin files" }

        if ($addins.Count -ge 1) {
            [xml] $m = Get-Content $addins[0].FullName
            $asm = $m.RevitAddIns.AddIn.Assembly
            if (-not (Test-Path (Join-Path $dir ($asm -replace '/', '\')))) {
                $problems += "manifest points at '$asm', which is not present"
            }
        }

        # PDBs are debug symbols; they have been shipping in every release ZIP.
        $pdb = @(Get-ChildItem $dir -Filter *.pdb -Recurse -File)
        if ($pdb) { $problems += "$($pdb.Count) PDB(s) still present" }

        if ($problems) { Fail "Revit $year payload" ($problems -join '; ') } else { Pass "Revit $year payload" }
    }
}

# ============================================================== 4. SERVER
Section 'MCP SERVER'
if ($SkipServer) {
    Write-Host '  SKIPPED (-SkipServer)' -ForegroundColor Yellow
} else {
    $server = Join-Path $repo 'server'
    if (-not (Test-Path (Join-Path $server 'node_modules'))) {
        Write-Host '  SKIPPED - node_modules absent. Run npm ci in server\.' -ForegroundColor Yellow
    } else {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'

        $tsc = & node (Join-Path $server 'node_modules\typescript\bin\tsc') --project $server 2>&1 | ForEach-Object { "$_" }
        $tscCode = $LASTEXITCODE
        if ($tscCode -ne 0 -or ($tsc | Where-Object { $_ -match 'error TS' })) {
            Fail 'tsc compiles clean' (($tsc | Select-Object -First 2) -join ' | ')
        } else { Pass 'tsc compiles clean' }

        $harnessArgs = @((Join-Path $server 'build\dynamo\selfTest.js'))
        if ($CorpusDir) { $harnessArgs += $CorpusDir }
        $h = & node @harnessArgs 2>&1 | ForEach-Object { "$_" }
        $ErrorActionPreference = $prev

        $line = ($h | Where-Object { $_ -match '^\d+ passed, \d+ failed' } | Select-Object -Last 1)
        if ($line -match '^(\d+) passed, (\d+) failed' -and $Matches[2] -eq '0') {
            Pass "dynamo harness ($($Matches[1]) checks)"
        } else {
            Fail 'dynamo harness' (($h | Where-Object { $_ -match '^FAIL' } | Select-Object -First 3) -join ' | ')
        }
    }
}

# ============================================================== 5. INSTALLER
Section 'POWERSHELL TOOLS - must run on Windows PowerShell 5.1'

# The installer shells out to powershell.exe, which is Windows PowerShell 5.1 on
# every supported Windows. PowerShell 7 is NOT guaranteed present. Two 7-only
# constructs shipped here undetected because the harness had only ever been run
# under 7:
#
#   - non-ASCII characters in a file with no BOM. 5.1 reads a BOM-less file as
#     ANSI, so an em-dash becomes mojibake and the PARSE fails outright.
#   - Set-Content -Encoding utf8NoBOM. That enum member does not exist on 5.1,
#     so every write throws at runtime while the file parses perfectly.
#
# Hence two checks: ASCII-only (static), and a real 5.1 parse of every script.
$psFiles = @(Get-ChildItem (Join-Path $PSScriptRoot '*.ps1') -File)
$nonAscii = @()
$sevenOnly = @()
foreach ($f in $psFiles) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    $bad = ($text.ToCharArray() | Where-Object { [int]$_ -gt 127 })
    if ($bad.Count -gt 0) { $nonAscii += "$($f.Name) ($($bad.Count) char(s))" }
    # Code lines only, and never this file: Verify.ps1's own comment names the
    # construct it is looking for, and a check that cannot tell code from the
    # comment explaining the code will fail on its own documentation.
    if ($f.Name -ne 'Verify.ps1') {
        $code = ($text -split "`r?`n" | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
        if ($code -match '-Encoding\s+utf8NoBOM') { $sevenOnly += "$($f.Name): -Encoding utf8NoBOM" }
    }
}
if ($nonAscii.Count -gt 0) {
    Fail 'shipped .ps1 files are ASCII-only' ($nonAscii -join '; ')
} else {
    Pass "shipped .ps1 files are ASCII-only ($($psFiles.Count) file(s))"
}
if ($sevenOnly.Count -gt 0) {
    Fail 'no PowerShell 7-only constructs in shipped .ps1' ($sevenOnly -join '; ')
} else {
    Pass 'no PowerShell 7-only constructs in shipped .ps1'
}

# A real 5.1 parse. Running the 5.1 parser from 7 is not the same thing as asking
# 5.1 itself, so this shells out to powershell.exe when it is available.
$ps51 = Get-Command powershell.exe -ErrorAction SilentlyContinue
if (-not $ps51) {
    Write-Host '  SKIP  Windows PowerShell 5.1 parse (powershell.exe not found)' -ForegroundColor Yellow
} else {
    $parseErrors = @()
    foreach ($f in $psFiles) {
        $out = & $ps51.Source -NoProfile -Command "
            `$e = `$null
            [void][System.Management.Automation.Language.Parser]::ParseFile('$($f.FullName)', [ref]`$null, [ref]`$e)
            if (`$e -and `$e.Count) { Write-Output `$e.Count } else { Write-Output 0 }" 2>&1
        $n = 0; [void][int]::TryParse(($out | Select-Object -Last 1), [ref]$n)
        if ($n -gt 0) { $parseErrors += "$($f.Name): $n error(s)" }
    }
    if ($parseErrors.Count -gt 0) {
        Fail 'every shipped .ps1 parses under Windows PowerShell 5.1' ($parseErrors -join '; ')
    } else {
        Pass "every shipped .ps1 parses under Windows PowerShell 5.1 ($($psFiles.Count) file(s))"
    }

    # And the config patcher the installer invokes must actually RUN there, not
    # merely parse. Its harness is self-contained and touches only temp files.
    $selftest = Join-Path $PSScriptRoot 'Set-RevitMcpTarget.selftest.ps1'
    if (Test-Path $selftest) {
        $prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $stOut = & $ps51.Source -NoProfile -ExecutionPolicy Bypass -File $selftest 2>&1
        $stCode = $LASTEXITCODE
        $ErrorActionPreference = $prevEap
        if ($stCode -ne 0) {
            Fail 'Set-RevitMcpTarget harness passes on Windows PowerShell 5.1' (($stOut | Select-Object -Last 1) -join ' ')
        } else {
            Pass 'Set-RevitMcpTarget harness passes on Windows PowerShell 5.1'
        }
    }
}
Section 'INSTALLER SCRIPT'

# Static checks first: they take a second and name every offence at once, where
# ISCC reports one per compile and a compile is minutes of lzma2 over 285 MB.
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& (Join-Path $PSScriptRoot 'Check-InstallerScript.ps1')
$lintCode = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($lintCode -ne 0) { $failed += 'installer script lint' } else { $passed += 2 }
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host '  SKIPPED - Inno Setup is not installed on this machine.' -ForegroundColor Yellow
} elseif (-not (Test-Path $stage)) {
    Write-Host '  SKIPPED - no staged payload to compile against.' -ForegroundColor Yellow
} else {
    # Compile for real. The [Code] section is Pascal Script and nothing else in
    # this gate parses it; both errors found here so far were in that section.
    $tmp = Join-Path $env:TEMP 'mcp-verify-iss'
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null

    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $log = & $iscc "/DAppVersion=$version" "/DPayloadDir=$stage" "/DOutDir=$tmp" `
                   (Join-Path $PSScriptRoot 'mcp-servers-for-revit.iss') 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev

    if ($code -ne 0) {
        Fail 'ISCC compiles the installer script' (($log | Where-Object { $_ -match 'Error' } | Select-Object -First 2) -join ' | ')
    } else {
        Pass 'ISCC compiles the installer script'
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ============================================================== verdict
Write-Host ''
if ($failed.Count) {
    Write-Host "$passed passed, $($failed.Count) FAILED" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "ALL PASS - $passed checks" -ForegroundColor Green
exit 0
