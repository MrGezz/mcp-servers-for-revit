# Changelog

## v1.0.1

The first release with an installer, Revit 2027 support, and a Dynamo channel.
The tool surface goes from **26 to 101**, and the whole source is English.

If you are upgrading from v1.0.0, the short version: **run the installer, then
restart your AI client and check the tool list.** Two of the most-reported issues
(`ECONNREFUSED` on connect, and "Failed to create command instance" repeating in
the log) were connection and logging defects, not configuration mistakes, and
both are fixed.

---

### Install

**New: a single `Setup.exe`.** Previously the only route was a ZIP dropped by
hand into `%APPDATA%\Autodesk\Revit\Addins\<year>\`, which is where three of the
open install issues came from.

The installer:

- ticks only the Revit versions actually present on the machine (found by
  `Revit.exe` on disk, not the registry, which keeps keys for versions you have
  uninstalled);
- refuses to run while Revit is open;
- removes the stale `revit-mcp.addin` left by v1.0.0, which could load a second
  copy of the add-in under a duplicate `ClientId`;
- checks for **Node.js 18+** and offers the download page if it is missing or too
  old — the MCP server is a Node program, and "installed but nothing works" was
  usually this;
- optionally registers the MCP server with Claude Desktop and Claude Code.

Files written by a setup program carry no `Zone.Identifier`, so there is nothing
to "unblock" — that failure mode is gone outright.

Per-version ZIPs are still published for anyone who prefers them.

### Supported Revit versions

**2020 through 2027.** 2027 is new.

> **Note for Revit 2026.5 and later.** Autodesk moved Revit 2026 onto the .NET 10
> runtime in a mid-cycle update (measured: `RevitAPI.dll` in 26.5.0.55 targets
> `.NETCoreApp v10.0`; 2026.0 shipped on .NET 8). The add-in ships as net8.0 for
> R26 and loads correctly on both, because .NET rolls forward. This only matters
> if you build the test project yourself — see *For contributors* below.

### New capabilities

- **Dynamo channel** — list, read, edit and run Dynamo graphs from the MCP
  server. `dynamo_op` shipped in v1.0.0 registered but unreachable; it is now
  five working tools.
- **Project memory / knowledge graph** — store and query project-scoped data.
  Persistent data now lives with the project instead of inside the `npx` cache,
  where it was silently discarded whenever the cache was cleared.
- **Localization** — every source file is English. The original Simplified
  Chinese strings are preserved as a selectable catalogue (639 strings: 451
  server-side, 188 command-set); set `REVIT_MCP_LOCALE=zh-Hans` to use them.
  Anything else means English.
- **`send_code_to_revit` transaction modes** — control whether the executed code
  runs inside a transaction.

### Fixed

**Connection**

- **`ECONNREFUSED` when the server and Revit are both running** (#29). Node
  resolves `localhost` to `::1` while the add-in listened on IPv4 only, so the
  two never met. Fixed on both sides.
- **"Failed to create command instance" repeating for every command** (#47, #48).
  Nothing was failing. The *success* path had been given the failure branch's log
  string, so a healthy startup printed it once per registered command. The two
  paths are now worded so they can never be confused.
- **Revit 2024 plugin not loading** (#12) — fixed previously, unreleased until now.

**Reliability**

- **Commands timing out on work that succeeded.** Every handler reset its
  completion signal *after* the external event was raised, so a fast-completing
  command had its own completion erased and the caller waited out the full
  timeout before throwing. Fixed in all 85 handlers.
- **A modal dialog could block every other command.** 33 `TaskDialog.Show` calls
  ran on the API thread, holding the event queue that all commands share. 20 were
  redundant and removed, 12 now report through the result, and `say_hello`'s is
  opt-in (`showDialog`, default off) and stripped from release builds entirely.
- **Two tools could return enormous responses** — `analyze_model_statistics`
  (measured 181,717 characters) and `get_material_quantities` (328,971), each
  over 90 % a single array. Both are now bounded, consistently across the
  handler, the command and the schema.
- **Concurrent requests for the same command could overwrite each other's
  parameters.** The registry keeps one handler instance per command name; the
  parameter hand-off is now guarded on all 86 commands.
- **A file handle was held on the plugin DLL for the life of the Revit session**,
  so updating the add-in required a full restart.

**Correctness**

- **Writes to read-only view parameters** silently failed the whole call in
  `set_view_properties`, `create_view` and `create_drafting_view`. A view whose
  scale is controlled by a template now warns instead of losing the operation.
- **Tagging in a 3D view** returned `success: true` with zero tags created and a
  list of raw Revit errors. It now says plainly that tags can only be created in
  a 2D view.
- **`create_revision`** lost the entire revision when the project used automatic
  numbering. The revision is now created and you are told the number could not be
  applied.
- **`connect_mep`** returned raw API text when connecting across MEP domains. It
  now names both elements and both domains.
- **`create_opening`** documented values (`Wall`, `Floor`, …) that did not
  deserialize. The schema and the enum now agree.
- **`create_ramp`** advertised a capability it does not have. Revit 2022–2027
  exposes no public ramp-creation API; the tool description now says so instead
  of failing at call time.
- **Curved walls and extrusion roofs** were unreachable through the MCP surface —
  no input could produce either. Both now have a path.
- Element ids, units and response casing are consistent across tools; several
  handlers returned internal feet where their siblings returned millimetres.

### Known issues

- **The installer and the add-in are unsigned.** SmartScreen shows *"Windows
  protected your PC"* on a machine that has not seen the installer — choose
  **More info → Run anyway**. On first launch after installing, Revit shows a
  **Security – Unsigned Add-In** prompt; choose **Always Load**. Revit will not
  finish starting until that prompt is answered. Both need an Authenticode
  certificate to remove; no packaging change can.
- **`create_ramp` is not supported** and cannot be, on any currently supported
  Revit version. Use the Revit UI.
- **Issue #31 (pyRevit ribbon tab disappears) could not be reproduced.** It was
  tested directly on Revit 2026.5 with `pyRevit.addin` and this add-in installed
  side by side in the same `Addins` folder — the configuration the report
  describes. The pyRevit tab loads normally. `Application.OnStartup` has been
  hardened regardless: it now catches, logs its own exceptions, and cannot take
  ribbon construction down for other add-ins. **If you can reproduce this,
  please reopen #31 with your Revit build and add-in list** — that is the missing
  piece.

### For contributors

- `tools/Verify.ps1` is the whole gate in one command: version consistency across
  the seven places years are declared, a build of every configuration, payload
  shape, TypeScript, the Dynamo harness, and a real Inno Setup compile.
- `tools/Package.ps1` and `tools/Make-Installer.ps1` produce the ZIPs and the
  `Setup.exe` locally, so a release can be inspected without pushing a tag.
- **The test suite does not cover this repository's code.** It has no
  `ProjectReference`; all 170 tests call the Revit API directly, so every handler
  here could be deleted and they would still pass. It is a useful Revit API smoke
  test and nothing more. See `docs/test-coverage-measured-2026-08-31.md`, which
  also documents the `-p:RevitTestTfm=` override you need to run the tests
  against Revit 2026.0–2026.2 rather than 2026.5.
- `docs/backlog-from-upstream-plans.md` records the inherited defect list,
  including two claims that were **tested and refuted** — acting on either would
  have broken working code.

---

## v1.0.0

Initial release. Revit 2020–2026, 26 MCP tools, per-version release ZIPs.
