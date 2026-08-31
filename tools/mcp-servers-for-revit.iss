; ============================================================================
;  mcp-servers-for-revit.iss - Inno Setup script for the Revit add-in.
;
;  Built by tools/Make-Installer.ps1, which stages the payload first and passes
;  the version, payload root and output directory in. Do not run ISCC on this
;  directly unless tools/Package.ps1 has already produced
;  dist\mcp-servers-for-revit <version>\Revit<year>\.
;
;  ------------------------------------------------------------------------
;  WHY AN INSTALLER AT ALL
;  ------------------------------------------------------------------------
;  Four of the open issues on this repository are installation failures, not
;  code failures:
;
;    #12  the DLLs were blocked by Windows because they came out of a
;         downloaded ZIP, so the CLR refused them with HRESULT 0x80131515
;    #12  two .addin files shared one ClientId, so Revit refused the second
;    #47  copying the tree by hand into the wrong level
;    #1   the MCP client half never got configured
;
;  An installer removes the first outright: files written by a setup program
;  are not marked with a Zone.Identifier stream, so there is nothing to unblock.
;  The rest it can check for and repair, which a ZIP cannot.
;
;  ------------------------------------------------------------------------
;  THIS DOES NOT INSTALL INTO {app}, AND NOTHING GOES TO PROGRAM FILES
;  ------------------------------------------------------------------------
;  Revit only loads add-ins from its own Addins folders, one per version, so the
;  payload goes to %APPDATA%\Autodesk\Revit\Addins\<year>\ and {app} holds only
;  the uninstaller and the docs - also under %APPDATA%.
;
;  PER-USER ONLY, DELIBERATELY. The all-users location is no longer one path:
;
;    2020-2026   %ProgramData%\Autodesk\Revit\Addins\<year>
;    2027+       C:\Program Files\Autodesk\Revit\Addins\<year>
;
;  Revit 2027 deprecated the ProgramData location outright - its journal says a
;  manifest there "won't be loaded" - and moved all-users add-ins under Program
;  Files. Note that the new path is NOT the versioned install directory
;  (C:\Program Files\Autodesk\Revit 2027\Addins\2027); that one is reserved for
;  Revit's own signed internal add-ins and rejects anything unsigned with
;  "not signed as internal addin".
;
;  Sources: Revit 2027 journal, quoted in pyrevitlabs/pyRevit#3275 (2026-04-07);
;  Autodesk Revit API forum, "Revit 2027 Add-in Installation folder changes"
;  (2026-04-23); help.autodesk.com/view/RVT/2027 guid f7165618.
;
;  %APPDATA%\Autodesk\Revit\Addins\<year> is the ONE location that works on
;  every version from 2020 to 2027, requires no administrator rights, and is
;  what the README already documents. pyRevit installs its payload under
;  %APPDATA% for the same reason. So this installer writes there and nowhere
;  else; an all-users deployment is a per-year branch plus elevation, which is
;  an IT-deployment concern rather than an end-user installer's.
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\dist\mcp-servers-for-revit 1.0.1"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

[Setup]
; A fixed GUID. Changing it makes Windows treat the next build as a DIFFERENT
; product, so upgrades stop replacing the old one and users collect duplicate
; entries in Apps & features.
AppId={{C4E81F27-6B3A-4D95-9E1C-7A2F5D08B6E3}
AppName=mcp-servers-for-revit
AppVersion={#AppVersion}
AppVerName=mcp-servers-for-revit {#AppVersion}
AppPublisher=mcp-servers-for-revit
AppPublisherURL=https://github.com/mcp-servers-for-revit/mcp-servers-for-revit
AppSupportURL=https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/issues
; NOT {autopf}. In an elevated install that is C:\Program Files, and a
; third-party Revit add-in has no business there - Program Files\Autodesk is
; where Autodesk's own signed products live. Only the uninstaller and the read-me
; land here anyway; the payload goes to the Addins folders below.
DefaultDirName={userappdata}\mcp-servers-for-revit
DefaultGroupName=mcp-servers-for-revit
DisableProgramGroupPage=yes
OutputDir={#OutDir}
OutputBaseFilename=mcp-servers-for-revit-{#AppVersion}-Setup
UninstallDisplayName=mcp-servers-for-revit {#AppVersion}

; VERSION INFO. Without these the Setup.exe carries no Windows file-version
; resource at all: right-click -> Properties -> Details is blank, and any tool
; that compares installed versions (including Windows itself, when deciding
; whether a file is newer) has nothing to read. AppVersion alone does NOT set
; them - it only drives the Add/Remove Programs entry.
;
; VersionInfoVersion must be numeric (x.x.x.x); a tag like "1.0.1-rc1" is
; rejected by the compiler, which is why the textual forms are separate.
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoDescription=mcp-servers-for-revit Setup
VersionInfoProductName=mcp-servers-for-revit
VersionInfoCompany=mcp-servers-for-revit
VersionInfoCopyright=MIT licensed. See LICENSE.
VersionInfoOriginalFileName=mcp-servers-for-revit-{#AppVersion}-Setup.exe
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Per-user, with no all-users option offered. Elevating would not help: the
; all-users path differs between Revit 2026 and 2027 (see the header), so a
; single "install for everyone" checkbox would be wrong for one of them. Lowest
; privileges, one destination, correct on every supported version.
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ----------------------------------------------------------------------------
; One component per Revit version. Nothing is preselected here - InitializeWizard
; ticks the versions actually found on the machine, because a list of eight
; checkboxes with no guidance is how people install the wrong one.
; ----------------------------------------------------------------------------
; A single iscustom type on purpose. A named type that lists every component
; would tick all eight years the moment it is chosen, which is the opposite of
; what the detection below is for. With only an iscustom type the checkboxes
; stay free and InitializeWizard preselects what is actually installed.
[Types]
Name: "custom"; Description: "Select Revit versions"; Flags: iscustom

[Components]
Name: "r2020"; Description: "Revit 2020"
Name: "r2021"; Description: "Revit 2021"
Name: "r2022"; Description: "Revit 2022"
Name: "r2023"; Description: "Revit 2023"
Name: "r2024"; Description: "Revit 2024"
Name: "r2025"; Description: "Revit 2025"
Name: "r2026"; Description: "Revit 2026"
Name: "r2027"; Description: "Revit 2027"

; The Revit add-in is only half the product: the other half is the MCP server
; the AI client launches. Shipping it here is what lets the installer register
; a config that points at THIS install, instead of leaving the user to run
; "npx -y mcp-server-for-revit" and get whatever is published on npm.
Name: "mcpserver"; Description: "MCP server (needs Node.js 18+)"; Types: custom

[Files]
; The add-in payload, one tree per Revit version, straight into that version's
; Addins folder. recursesubdirs+createallsubdirs preserves
; revit_mcp_plugin\Commands\RevitMCPCommandSet\<year>\ exactly as built.
Source: "{#PayloadDir}\Revit2020\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2020"; Components: r2020; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2021\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2021"; Components: r2021; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2022\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022"; Components: r2022; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Components: r2023; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Components: r2024; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: r2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Components: r2026; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\Revit2027\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; Components: r2027; Flags: ignoreversion recursesubdirs createallsubdirs

; The compiled MCP server and the script that registers it. Both land under
; {app} so that Set-RevitMcpTarget.ps1 -RepoRoot "{app}" resolves
; {app}\server\build\index.js exactly as it resolves the repository layout.
Source: "{#PayloadDir}\server\*"; DestDir: "{app}\server"; Components: mcpserver; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\tools\Set-RevitMcpTarget.ps1"; DestDir: "{app}\tools"; Components: mcpserver; Flags: ignoreversion

; Docs go to {app}, which is also where the uninstaller lives.
Source: "{#PayloadDir}\READ ME FIRST.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Tasks]
; Off by default would be the wrong default here - registering the server is the
; step users most often miss, and it is reversible from the uninstaller. It is
; still a TASK rather than unconditional, because it writes a file owned by
; another application.
Name: "registermcp"; Description: "Register the MCP server with Claude Desktop and Claude Code"; Components: mcpserver

[Icons]
Name: "{group}\mcp-servers-for-revit on GitHub"; Filename: "https://github.com/mcp-servers-for-revit/mcp-servers-for-revit"
Name: "{group}\Read me"; Filename: "{app}\READ ME FIRST.txt"

[UninstallDelete]
; Inno removes the files it installed, but the plugin writes logs and a command
; registry beside itself at runtime (Logs\, commandRegistry.json). Without these
; lines the folder survives uninstall holding stale state, and a later reinstall
; inherits a registry written by a different version.
;
; EVERY LINE IS SCOPED TO ITS COMPONENT. An unscoped list would delete the
; revit_mcp_plugin folder for all eight years regardless of which were installed
; - so a user who installed only 2027 through this installer, having previously
; copied 2022 in by hand, would lose the 2022 one on uninstall. That is the
; defect in PR #49's uninstall loop (for I := 2020 to 2026, unguarded), and it
; is not worth reproducing.
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2020\revit_mcp_plugin"; Components: r2020
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2021\revit_mcp_plugin"; Components: r2021
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2022\revit_mcp_plugin"; Components: r2022
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2023\revit_mcp_plugin"; Components: r2023
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2024\revit_mcp_plugin"; Components: r2024
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2025\revit_mcp_plugin"; Components: r2025
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2026\revit_mcp_plugin"; Components: r2026
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2027\revit_mcp_plugin"; Components: r2027

[Code]
{ Everything below this point is Pascal Script, where ';' separates statements
  and is NOT a comment marker - only braces and // are. A ';' comment here parses
  as code and ISCC stops with a column number and no explanation.

  Inno's Pascal Script also has no typed array constants, so the supported range
  is two integers rather than a list. Keep it in step with [Components], [Files]
  and Package.ps1's $AllYears; tools\Verify.ps1 asserts all four agree. }
const
  FirstYear = 2020;
  LastYear  = 2027;

{ ------------------------------------------------------------------------
  DETECTION.

  Revit.exe on disk, NOT the registry, and not the Addins folders.

  Measured on a real machine: HKLM\SOFTWARE\Autodesk\Revit listed 2021 and 2023
  as products when neither was installed - the keys survive an uninstall - while
  %ProgramData%\Autodesk\Revit\Addins held year folders back to 2017. Both
  sources OVER-REPORT, and an installer that trusts either offers to install for
  Revit versions that are not there.

  The presence of the executable is the one signal that matched reality.
  ------------------------------------------------------------------------ }
// The 64-bit Program Files, whichever install mode we are in.
//
// {commonpf} is NOT it. In a 32-bit installer - the default, and what this
// compiles to - {commonpf} expands to "C:\Program Files (x86)", where Revit has
// never been installed. Measured: the guard detected 0 of the 4 Revit versions
// present on the test machine. {commonpf64} is only legal in 64-bit install
// mode, hence the IsWin64 branch.
//
// These are // comments, not { }, deliberately: Pascal brace comments do not
// nest, so a { } comment mentioning {commonpf} ends at that constant's own
// closing brace and everything after it is parsed as code. Verify.ps1 checks
// for that shape now, because it is invisible until ISCC gives you a column
// number in a file that looks fine.
function ProgramFiles64(): string;
begin
  if IsWin64 then
    Result := ExpandConstant('{commonpf64}')
  else
    Result := ExpandConstant('{commonpf}');
end;

function RevitInstalled(const Year: string): Boolean;
begin
  Result := FileExists(ProgramFiles64() + '\Autodesk\Revit ' + Year + '\Revit.exe');
end;

function DetectedYears(): string;
var
  Y: Integer;
begin
  Result := '';
  for Y := FirstYear to LastYear do
    if RevitInstalled(IntToStr(Y)) then
    begin
      if Result <> '' then Result := Result + ',';
      Result := Result + 'r' + IntToStr(Y);
    end;
end;

{ ------------------------------------------------------------------------
  Refuse to install while Revit is running.

  Revit loads the add-in assemblies into its process and holds them. Writing
  over a loaded DLL fails, and Inno's default response to a locked file is to
  schedule a replace-on-reboot - which leaves a half-updated install that starts
  and then fails on a version mismatch nobody can see.

  tasklist rather than a mutex: Revit's mutex names are undocumented and change
  between versions, so AppMutex cannot be relied on here.
  ------------------------------------------------------------------------ }
function RevitIsRunning(): Boolean;
var
  ResultCode: Integer;
  TempFile: string;
  Output: AnsiString;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\revit-running.txt');

  { Capture what tasklist PRINTS and search it, rather than piping into 'find'
    and reading an exit code. The pipe version returned 0 from a shell and still
    reported "not running" from inside Exec - and a guard that is wrong in the
    permissive direction is the worst kind here: it lets the installer overwrite
    DLLs that a running Revit has loaded. This version is inspectable; the file
    is there to read if it ever disagrees with reality again. }
  if Exec(ExpandConstant('{cmd}'),
          '/C tasklist /FI "IMAGENAME eq Revit.exe" /NH > "' + TempFile + '"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TempFile, Output) then
      Result := Pos('Revit.exe', Output) > 0;
    DeleteFile(TempFile);
  end;
end;

{ ------------------------------------------------------------------------
  MCP CLIENT REGISTRATION.

  Writes the "mcp-server-for-revit" entry into the AI client's config so the
  user does not have to run a second script by hand. The JSON editing itself
  lives in tools\Set-RevitMcpTarget.ps1, which is shipped beside the server
  and already has a test harness; this only invokes it and reads its exit code.

  Windows PowerShell 5.1 is used deliberately: it is present on every supported
  Windows, whereas pwsh 7 may not be. The script was checked for 7-only syntax.

  Exit codes, from the script:
    0  wrote (or had nothing to write)
    1  one or more configs failed
    2  the target layout was wrong
    3  Claude Desktop is RUNNING - the config is application-owned state and an
       edit made now is silently discarded when the app exits, so refusing is
       correct and the user is told to close it and re-run.
  ------------------------------------------------------------------------ }
{ DEPENDENCY CHECK. Presence is not enough - the server needs Node 18+, and a
  machine with Node 12 on PATH would otherwise pass a bare "is it there" test
  and then fail at runtime with a syntax error nobody can place.

  Exec cannot capture stdout, so the version is redirected to a file and read
  back. Returns -1 when Node is absent or unreadable, and reports the raw
  string so the user is told what WAS found rather than just "too old". }
function NodeMajor(var Raw: string): Integer;
var
  Tmp, S: string;
  A: AnsiString;
  Code, Dot: Integer;
begin
  Result := -1;
  Raw := '';
  Tmp := ExpandConstant('{tmp}') + '\nodever.txt';
  if not Exec(ExpandConstant('{cmd}'), '/C node --version > "' + Tmp + '" 2>&1',
              '', SW_HIDE, ewWaitUntilTerminated, Code) then Exit;
  if (Code <> 0) or (not FileExists(Tmp)) then Exit;
  if not LoadStringFromFile(Tmp, A) then Exit;
  S := Trim(String(A));
  Raw := S;
  if S = '' then Exit;
  if S[1] = 'v' then Delete(S, 1, 1);
  Dot := Pos('.', S);
  if Dot > 1 then S := Copy(S, 1, Dot - 1);
  Result := StrToIntDef(S, -1);
end;

{ Reported on the components page, where the choice is still changeable, rather
  than after the files have landed. Never BLOCKS: a user may be installing the
  add-in now and Node later, and refusing that would be wrong. }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Raw, Msg: string;
  Major, Answer: Integer;
begin
  Result := True;
  if CurPageID <> wpSelectComponents then Exit;
  if not WizardIsComponentSelected('mcpserver') then Exit;

  Major := NodeMajor(Raw);
  if Major >= 18 then Exit;

  if Major < 0 then
    Msg := 'Node.js was not found on this PC.' + #13#10#13#10 +
           'The MCP server is a Node program: without Node.js 18 or newer your AI' + #13#10 +
           'client will not be able to start it. The Revit add-in itself does not' + #13#10 +
           'need Node and will work either way.'
  else
    Msg := 'Node.js ' + Raw + ' was found, but the MCP server needs 18 or newer.' + #13#10#13#10 +
           'Setup will continue, but your AI client will not be able to start the' + #13#10 +
           'server until Node.js is upgraded.';

  Answer := SuppressibleMsgBox(Msg + #13#10#13#10 +
              'Open the Node.js download page now?' + #13#10 +
              '(Yes opens a browser; No continues the installation.)',
              mbConfirmation, MB_YESNO, IDNO);
  if Answer = IDYES then
    ShellExecAsOriginalUser('open', 'https://nodejs.org/en/download', '', '',
                            SW_SHOWNORMAL, ewNoWait, Answer);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  if RevitIsRunning() then
  begin
    if SuppressibleMsgBox(
         'Revit is running.' + #13#10#13#10 +
         'The add-in files are loaded by Revit while it is open, so they cannot be replaced. ' +
         'Close every Revit window and then click Retry.' + #13#10#13#10 +
         'Cancel will stop the installation.',
         mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;

    { One retry, then stop. Looping here would hang an unattended install. }
    if RevitIsRunning() then
    begin
      SuppressibleMsgBox('Revit is still running. Installation cancelled.', mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  end;

  if DetectedYears() = '' then
  begin
    if SuppressibleMsgBox(
         'No Revit installation was found on this PC.' + #13#10#13#10 +
         'You can still continue and choose versions by hand - useful if Revit is installed somewhere unusual - ' +
         'but nothing will load until a matching Revit is present.' + #13#10#13#10 +
         'Continue anyway?',
         mbConfirmation, MB_YESNO, IDNO) = IDNO then
      Result := False;
  end;
end;

procedure InitializeWizard();
var
  Detected, Raw: string;
begin
  Detected := DetectedYears();
  if Detected <> '' then
    WizardSelectComponents(Detected);

  { Preselect the MCP server only when this PC can actually run it. Ticking a
    component whose dependency is missing invites an install that looks complete
    and does nothing; leaving it unticked with the reason on the page is honest.
    The user can still tick it by hand, and NextButtonClick explains the cost. }
  if NodeMajor(Raw) >= 18 then
    WizardSelectComponents('mcpserver');
end;

{ ------------------------------------------------------------------------
  Clear a stale second manifest before installing.

  Release v1.0.0 shipped TWO .addin files carrying the same ClientId -
  mcp-servers-for-revit.addin and revit-mcp.addin. Revit refuses the second with
  "client id ... is already loaded in session", and the user sees only "Revit
  cannot run the external application". Fixed in the repository by #19, but any
  machine that installed v1.0.0 by hand still has the orphan sitting there, and
  installing over the top would leave it.

  Deleting only that exact filename, only in the years being installed to.
  ------------------------------------------------------------------------ }
procedure RemoveStaleManifests();
var
  Y: Integer;
  Base, Stale: string;
begin
  Base := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\';
  for Y := FirstYear to LastYear do
  begin
    if not WizardIsComponentSelected('r' + IntToStr(Y)) then Continue;
    Stale := Base + IntToStr(Y) + '\revit-mcp.addin';
    if FileExists(Stale) then
      DeleteFile(Stale);
  end;
end;

procedure RegisterMcpServer();
var
  Script, Params: string;
  Code: Integer;
begin
  Script := ExpandConstant('{app}\tools\Set-RevitMcpTarget.ps1');
  if not FileExists(Script) then Exit;

  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '"' +
            ' -Apply -AddIfMissing -IncludeClaudeCode -RepoRoot "' +
            ExpandConstant('{app}') + '"';

  if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    SuppressibleMsgBox('Could not start Windows PowerShell to register the MCP' + #13#10 +
      'server. Register it by hand:' + #13#10#13#10 +
      '  powershell -File "' + Script + '" -Apply -AddIfMissing',
      mbError, MB_OK, IDOK);
    Exit;
  end;

  if Code = 3 then
    SuppressibleMsgBox('Claude Desktop is running, so the MCP server was NOT' + #13#10 +
      'registered.' + #13#10#13#10 +
      'That config file belongs to the application: it is rewritten when the app' + #13#10 +
      'exits, so an edit made now would appear to work and then vanish.' + #13#10#13#10 +
      'Close Claude Desktop completely, then run:' + #13#10 +
      '  powershell -File "' + Script + '" -Apply -AddIfMissing',
      mbInformation, MB_OK, IDOK)
  else if Code <> 0 then
    SuppressibleMsgBox('Registering the MCP server reported a problem (exit ' +
      IntToStr(Code) + ').' + #13#10#13#10 +
      'The Revit add-in is installed and unaffected. To see what went wrong, run' + #13#10 +
      'the same command in a console:' + #13#10 +
      '  powershell -File "' + Script + '" -Apply -AddIfMissing',
      mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { ssInstall, before the files land: the orphan must be gone by the time Revit
    next starts, and doing it here means a failed install does not leave the new
    manifest beside the old one. }
  if CurStep = ssInstall then
    RemoveStaleManifests();

  { ssPostInstall, after the server and the script are on disk - the script
    resolves the app folder's server\build\index.js and refuses if it is
    missing. Naming that constant with its braces here would END THIS COMMENT
    at the constant's own brace, which is the r130.1 Pascal Script trap. }
  if (CurStep = ssPostInstall) and WizardIsComponentSelected('mcpserver')
     and WizardIsTaskSelected('registermcp') then
    RegisterMcpServer();
end;

function InstalledSummary(): string;
var
  Y: Integer;
begin
  Result := '';
  for Y := FirstYear to LastYear do
    if WizardIsComponentSelected('r' + IntToStr(Y)) then
    begin
      if Result <> '' then Result := Result + ', ';
      Result := Result + IntToStr(Y);
    end;
end;

function McpClientSummary(): string;
begin
  if not WizardIsComponentSelected('mcpserver') then
    Result :=
      '  The MCP server component was not installed, so no client was' + #13#10 +
      '  configured. Install it, or register the published package yourself:' + #13#10 +
      '    claude mcp add --scope user mcp-server-for-revit -- cmd /c npx -y mcp-server-for-revit'
  else if WizardIsTaskSelected('registermcp') then
    Result :=
      '  Claude Desktop and Claude Code were pointed at the server installed' + #13#10 +
      '  here. RESTART your AI client, then confirm by the TOOL LIST' + #13#10 +
      '  rather than by the config file - if the app was running during setup,' + #13#10 +
      '  the edit was refused and you were told so.'
  else
    Result :=
      '  You chose not to register the server. To do it later:' + #13#10 +
      '    powershell -File "' + ExpandConstant('{app}\tools\Set-RevitMcpTarget.ps1') + '" -Apply -AddIfMissing';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { The one thing people forget: the Revit add-in is only half of the product.
    Said on the last page, where it is read, rather than in a README. }
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption :=
      'mcp-servers-for-revit was installed for Revit ' + InstalledSummary() + '.' + #13#10 +
      'Location: %APPDATA%\Autodesk\Revit\Addins\ (this user only).' + #13#10#13#10 +
      'IN REVIT' + #13#10 +
      '  Start Revit. If it asks about an unknown add-in, choose Always Load.' + #13#10 +
      '  Then Add-Ins ribbon > Revit MCP Switch to start the server.' + #13#10#13#10 +
      'IN YOUR AI CLIENT' + #13#10 +
      McpClientSummary();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
    SuppressibleMsgBox(
      'mcp-servers-for-revit has been removed from %APPDATA%\Autodesk\Revit\Addins.' + #13#10#13#10 +
      'Not touched:' + #13#10 +
      '  - the MCP server registration in your AI client. If setup wrote it,' + #13#10 +
      '    undo it BEFORE deleting this folder with:' + #13#10 +
      '      powershell -File "<install folder>\tools\Set-RevitMcpTarget.ps1" -Revert -Apply' + #13#10 +
      '  - any Revit models or data',
      mbInformation, MB_OK, IDOK);
end;
