<#
    Check-InstallerScript.ps1 - static checks on the [Code] section of the .iss.

    WHY THIS EXISTS. Three defects in one round, all in [Code], all invisible on
    the page, all reported by ISCC as nothing but a line and a column:

      1. ';' used as a comment marker. It is the comment marker everywhere else
         in a .iss file; inside [Code] it is a statement separator, so the prose
         was parsed as code.

      2. a typed array constant - "array[0..7] of string". Pascal Script has no
         such thing.

      3. a brace comment mentioning {commonpf}. Pascal's { } comments DO NOT
         NEST, so the comment ended at that constant's own closing brace and
         everything after it became code.

    ISCC finds these one per compile, and a compile of this installer is minutes
    of lzma2 over 285 MB. This finds all of them in under a second.

    It is a linter, not a parser: it cannot prove the script is correct, only
    that these three shapes are absent. The real compile still runs in Verify.ps1.

        powershell -ExecutionPolicy Bypass -File .\tools\Check-InstallerScript.ps1
#>

[CmdletBinding()]
param(
    [string] $Path
)

$ErrorActionPreference = 'Stop'
if (-not $Path) { $Path = Join-Path $PSScriptRoot 'mcp-servers-for-revit.iss' }
if (-not (Test-Path $Path)) { throw "No installer script at '$Path'." }

$text   = Get-Content $Path -Raw
$codeAt = $text.IndexOf('[Code]')
if ($codeAt -lt 0) { throw "'$Path' has no [Code] section." }

$lf       = [char]10
$code     = $text.Substring($codeAt)
$offset   = ($text.Substring(0, $codeAt) -split $lf).Count
$lines    = $code -split $lf

$problems = @()

# --- 1. ';' comments --------------------------------------------------------
for ($i = 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*;') {
        $problems += "line $($offset + $i): ';' is a statement separator in Pascal Script, not a comment. Use // or { }."
    }
}

# --- 2. typed array constants ----------------------------------------------
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'array\s*\[') {
        $problems += "line $($offset + $i): Pascal Script has no typed array constants."
    }
}

# --- 3. brace comments closed early by an Inno constant ---------------------
# A multi-line brace comment whose closing brace is followed, on the same line,
# by more prose is a comment that ended somewhere the author did not intend.
$idx = 0
while ($true) {
    $open = $code.IndexOf('{', $idx)
    if ($open -lt 0) { break }
    $close = $code.IndexOf('}', $open + 1)
    if ($close -lt 0) { break }

    $inner = $code.Substring($open + 1, $close - $open - 1)
    if ($inner.Contains($lf)) {
        $nl   = $code.IndexOf($lf, $close + 1)
        $tail = if ($nl -gt 0) { $code.Substring($close + 1, $nl - $close - 1) } else { '' }
        if ($tail -match '[A-Za-z]{3}') {
            $line = $offset + ($code.Substring(0, $open) -split $lf).Count - 1
            $problems += "line ${line}: brace comment closes early (at '$($inner.Trim().Split($lf)[-1])}') and '$($tail.Trim())' is then parsed as code. Brace comments do not nest - use //."
        }
    }
    $idx = $close + 1
}

# --- 4. install destinations -----------------------------------------------
#
# Where a Revit add-in may be written is version-dependent, and getting it wrong
# fails silently - Revit simply does not load the add-in.
#
#   %APPDATA%\Autodesk\Revit\Addins\<year>        works on 2020-2027, no admin
#   %ProgramData%\Autodesk\Revit\Addins\<year>    all-users 2020-2026 ONLY;
#                                                  Revit 2027's journal says a
#                                                  manifest here "won't be loaded"
#   C:\Program Files\Autodesk\Revit\Addins\<year> all-users 2027+
#   C:\Program Files\Autodesk\Revit <year>\Addins\ reserved for Autodesk's own
#                                                  SIGNED add-ins; unsigned ones
#                                                  are rejected outright
#
# {autoappdata} resolves to %ProgramData% in an elevated install, so it is not a
# safe destination for a single-path installer. This checks DestDir lines only -
# reading Program Files to DETECT an installed Revit is fine and expected.
$destLines = [regex]::Matches($text, 'DestDir:\s*"([^"]+)"')
foreach ($m in $destLines) {
    $dest = $m.Groups[1].Value
    if ($dest -match '\{(autoappdata|commonappdata)\}') {
        $problems += "DestDir '$dest' resolves to %ProgramData% in an elevated install, which Revit 2027 does not load."
    }
    if ($dest -match '\{(autopf|commonpf|pf|pf32|pf64|commonpf64)\}') {
        $problems += "DestDir '$dest' writes under Program Files; a third-party Revit add-in belongs in %APPDATA%."
    }
}
if (-not ($problems | Where-Object { $_ -like 'DestDir*' })) {
    Write-Host "  PASS  install destinations: no %ProgramData% or Program Files targets" -ForegroundColor Green
}

if ($problems) {
    Write-Host "  FAIL  [Code] section" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkRed }
    exit 1
}

Write-Host "  PASS  [Code] section: no ';' comments, no typed array constants, no early-closed brace comments" -ForegroundColor Green
exit 0
