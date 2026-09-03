# Changelog

## v1.0.2

Token cost, small-model reliability, and a Dynamo that starts itself.

### Token usage

- **Lean tool profile.** The catalogue an MCP client sends to the model on every
  request measured 101 tools / 115 KB / ~32,000 tokens. The server now enables a
  core of 14 tools (~3,000 tokens; the full catalogue itself went from ~32,000 to ~25,000) and exposes the rest through a new
  `revit_tools` tool that lists groups and enables them on demand, with
  `notifications/tools/list_changed` so the client refreshes at once.
  `REVIT_MCP_PROFILE=full` restores the old behaviour; `REVIT_MCP_TOOLS` adds
  groups at start-up. See *Token usage and tool profiles* in the README.
- **Every tool description and schema rewritten to be short.** Units are
  stated once per tool, obvious property descriptions are gone, and repeated
  `{x,y,z}` / id fragments come from one shared module. The schema *structure*
  (names, types, optionality, enums, defaults) was left unchanged by this pass —
  a checker diffed every tool's JSON schema before and after with descriptions
  stripped; the audit repair further down then changed some shapes on purpose.
- **Compact results.** No more 2-space-indented JSON; nulls, empty strings and
  `UniqueId` are pruned; floats are rounded to 0.001 mm; listing tools default
  to 30 records (was 100) and report the total; any result over
  `REVIT_MCP_MAX_RESULT_CHARS` (20,000) has its dominant array cut to whole
  records and marked `_truncated`. Measured on `get_current_view_elements`
  against a 6,909-element view: the same 100-record call went from 34.4 KB to
  16.5 KB, and the new default (30 records) is 4.2 KB.
- **Server `instructions`.** Seven operating rules (units, never invent ids,
  how to enable more tools, what an error looks like, small limits, undo
  semantics, what to tell the user when Revit is unreachable) are handed to
  the client at initialise time, where most clients place them in the system
  prompt.

### Fixed

- **Six commands answered with the PREVIOUS call's result.** `dynamo_op`,
  `delete_element`, `get_selected_elements`, `get_current_view_info`,
  `get_available_family_types` and `say_hello` never reset their completion
  signal before raising the external event, so every call after the first
  returned immediately with stale data — measured live: an `open` reply came
  back to a `status` request, and the next `status` got the `open` failure.
  For `delete_element` that meant reporting the wrong ids as deleted; for
  `get_selected_elements`, the previous selection. All six now reset.
- **A failed read stayed failed.** `get_available_family_types`,
  `get_current_view_info` and `get_selected_elements` kept the error from the
  previous call (the field was never cleared), so after one unknown category
  name every later call failed with that same message until Revit restarted.
  Found live during the 1.0.2 retest.
- The plugin attached its "failed on a background thread ... accesses the
  Revit API without marshalling" diagnosis to every command error, including
  deliberate ones such as an unknown category name. It is now reserved for
  Revit API exceptions; other errors are reported as written.
- **Failures were reported as successes.** Only 5 of 101 tools set `isError`;
  the rest returned "X failed: ..." as ordinary text, which small models read
  as done. Every failure path now returns `isError` with `{"ok": false,
  "error": ...}`, including Revit replies of the shape `{success: false}`,
  `{ok: false}` and `{Success: false}`.
- **Thirteen more batch creators reported success with nothing created.** The
  audit sweep added the skip reasons to `create_text_note`, `create_column`,
  `create_surface_based_element`, `create_conduit`, `create_direct_shape`,
  `create_equipment`, `create_mep_system`, `create_pipe`, `create_space`,
  `create_swept_shape`, `create_schedule`, `create_sheet` and `create_view`
  but left `Success = true` on the result; found by the pre-release review.
  They now fail when the list is empty, and six others no longer say
  "Successfully created 0" on a failure.
- `create_dimensions` `options` dropped integer values silently (JSON
  integers deserialise as `long`, the handler tested for `int`); `create_group`
  no longer reports an empty batch as success; `create_detail_curve`'s
  description no longer restricts it to drafting and plan views.
- `export_views` listed every PNG/JPG written so far in the batch once per
  view when several views shared a `fileName`; it now reports only the files
  each export produced. `create_tag` reports an element Revit silently
  refused to tag (a null from `IndependentTag.Create`) as a warning and a
  failure when nothing was tagged. `create_detail_curve` no longer reports a
  partial batch as a failure.
- `manage_project_parameters` `list` answered `Group: "PG_DATA", Visible: true`
  for every parameter on Revit 2023 and later: the branch that read the real
  values sat behind an `#elif` that could never be reached. It now reports the
  group's display label and the definition's visibility.
- A tool that is in no catalogue group was never enabled, even with
  `REVIT_MCP_PROFILE=full` (the check compared group names against the
  word "full"). No shipped tool is in that state; the check is fixed.
- `delete_element` accepts numeric ids as well as strings (every other tool
  uses numbers, so models sent numbers and got a validation error).
- `ai_element_filter` described `boundingBoxMin`/`boundingBoxMax` as two-point
  lines (`{p0, p1}`) while the handler deserialises a single `{x, y, z}` point,
  so every spatial filter became the empty box at the origin and matched
  nothing. The schema now matches the handler.

**Units and promises (from a tool-by-tool audit of the C# handlers against the
tool descriptions)**

- **Results now carry metric values with the unit in the key.** `export_room_data`
  returned `area`/`volume`/`perimeter`/`unboundedHeight` in Revit's internal
  square feet, cubic feet and feet under a tool that promised millimetres;
  `get_material_quantities` did the same for `area`/`volume`; and
  `get_current_view_elements` wrote `LocationX/Y/Z`, `Start`, `End` and `Length`
  in feet — numbers a model then fed back into `create_wall` as millimetres.
  They are now `areaM2`, `volumeM3`, `perimeterMm`, `unboundedHeightMm`,
  `totalAreaM2`, `totalVolumeM3`, `LocationMm`, `StartMm`, `EndMm`, `LengthMm`.
  `get_current_view_elements` also resolves Family/Type to names instead of raw
  ids and reports doubles as Revit display strings ("3000 mm").
- `export_room_data`: `includeUnplacedRooms` and `includeNotEnclosedRooms` both
  tested the same condition, so either flag alone still returned nothing. They
  now mean what they say (no location / placed with zero area), and each room
  carries a `status`.
- `get_available_family_types` and `get_material_quantities` silently dropped
  unknown category names — and when every name was unknown, dropped the filter
  and returned the whole project as a match. An unknown name is now an error
  with a "did you mean" hint, as `get_current_view_elements` already did.
- `delete_element` parsed ids as 32-bit integers; Revit 2024+ ids above
  2,147,483,647 were reported as "unparseable" and never deleted.
- `create_point_based_element` reported success when every item had been
  skipped and nothing was placed; it now fails with the skip reasons. Its
  schema gains the `category` the handler needs to pick a type when `typeId`
  is omitted, and loses `width`/`depth`, which the handler never read.
- `operate_element` advertised a `Highlight` action that does not exist in the
  handler; the action list is now an enum of the real ones.
- `create_line_based_element` claimed to create pipes (it handles walls, ducts
  and family-based categories); `create_surface_based_element` implied that
  `thickness` sets the element thickness (it only positions the top);
  `say_hello` implied `showDialog` works in release builds (debug only);
  `ai_element_filter` implied a bounding box alone is a valid query (it needs
  another filter). The descriptions now say what the handlers do.
- `send_code_to_revit` returns the snippet's value as data instead of an
  escaped JSON string, and says in its description that the snippet body sees
  internal feet.

**Tool-by-tool repair (see [AUDIT-2026-09-03.md](AUDIT-2026-09-03.md))**

Every one of the 86 Revit commands was audited by comparing its MCP tool
(description and schema) against the C# command, handler and model that run
it; 182 findings survived an independent adversarial verification, and 184
fixes were applied across 70 commands. The recurring shapes:

- **Tools that could never work.** The TypeScript sent keys the C# model never
  read: `create_reference_plane` (`startPoint`/`endPoint` vs `bubbleEnd`/`freeEnd`
  — every call created zero planes and reported success), `create_group`
  (`groupName` vs `name`), `create_column` (`columnType` vs `type`),
  `create_railing` (`baseLevel` vs `level`), `create_model_curve`
  (`startPoint`/`endPoint`/`sketchPlaneLevel` vs a `points` array and
  `sketchPlaneId`), `create_stair` (`location`/`direction` vs
  `startPoint`/`endPoint`/`pathPoints`), `create_roof` (an `options`
  dictionary), `create_opening` (`sillHeight`). The schemas now match the
  handlers.
- **Success with nothing done.** Around 40 handlers returned `Success: true`
  with an empty result when every item had been skipped (level not found, type
  not found, element not found, nothing matched). They now fail with the skip
  reasons; partial batches still report partial success.
- **Parameters the handler never read** (`thickness`, `material`, `width`,
  `depth`, `height`, `levelName`, `overhang`, schedule `fields`/`filters`/
  `sortFields`/`groupFields`, `labelText`, `ductType`, `pipeType`, and more)
  are gone from the schemas or marked as ignored, so a model can no longer
  believe it set them. Implementing them is listed as deferred work.
- **Null dereferences before the null check** (`baseLevel.Elevation` before
  `if (baseLevel == null)`) in the duct, pipe, conduit, equipment, line- and
  surface-element handlers aborted whole batches; they now skip the item.
- **Units and enums.** `query_geometry`, `query_view_range`,
  `query_references`, `analyze_model_statistics` (level elevation) now return
  metric values with the unit in the key (`MinMm`/`MaxMm`, `AreaM2`,
  `SurfaceAreaM2`, `VolumeM3`, `LengthMm`, `OffsetMm`, `elevationMm`) instead
  of internal feet; `create_mep_curve` passes the level nearest the requested
  elevation to Revit instead of ignoring the `level` parameter and handing
  `Duct.Create` / `Pipe.Create` an invalid level id; `set_element_curve` and
  `set_parameters` convert millimetre inputs to feet; text-note rotation is
  converted from degrees to radians; `query_geometry.detailLevel` uses Revit's
  real enum values; `place_view_on_sheet.rotation` is the viewport rotation
  enum, not degrees; `create_section_view` and `set_view_range` defaults match
  the handler's.
- **Descriptions now say what happens**: `connect_mep` connection types are
  logged only, `create_callout` needs a Section view type, `load_family` and
  `manage_*` return booleans, `manage_graphics_resources` only updates
  existing styles, `create_space.spaceType` writes the Department parameter,
  and so on.
- `project_memory_*` and the legacy `store_*`/`query_stored_data` tools now
  surface a C# `Success: false` as an error, and `rooms_by_project_name`
  returns the rooms rather than the project entity.

### Dynamo

- **`dynamo_run_graph` honours its timeout and its path.** `timeout_seconds`
  was accepted and ignored by the command set (which used a fixed 10 minutes);
  it now travels to Revit as `timeoutMs` and the socket client waits at least
  as long. `run` used to evaluate whatever workspace was open; it now refuses
  when the open workspace is not the requested `.dyn`.
- **A closed Dynamo window no longer looks like a running Dynamo.** Closing
  the window keeps Dynamo's model object alive but disposes its engine, and
  every `open` after that failed with a bare NullReferenceException while
  `status` still said reachable. The add-in now checks the engine; a dead
  one reports as not running, and `dynamo_run_graph` launches Dynamo again
  by itself.
- **`dynamo_run_graph` says how the run went.** After `run` the server
  polls a new `eval_status` op until Dynamo's evaluation count moves, then
  returns every node that did not end Active with its messages. A node that
  ended in Warning or Error (a Python exception, say) makes the result an
  error. Before, "Run requested" was all a caller got, and a graph whose only
  node had failed looked identical to one that worked. Measured on Dynamo
  3.6: the failed node's exception text is in the result. A run whose
  evaluation has not finished within `timeout_seconds` is reported as an
  error too ("may still be in progress"), not as `ok`.
- **`dynamo_edit_graph` places library nodes.** New `add_function_node`
  operation adds any stock or package node by its DesignScript signature
  (`DSOffice.Data.OpenXMLExportExcel@string,string,var[][],int,int,bool,bool`,
  say) with named ports, so a generated graph uses the same nodes a person
  would drag from the library, not only Code Blocks and Python. Signatures
  are visible in `dynamo_read_graph` output for any graph that uses the node.
- **`dynamo_edit_graph` can start from nothing.** `create: true` starts from a
  new, empty Dynamo 3 graph when the path does not exist, so "create a graph
  that ..." works from a client that cannot write files itself; the
  operations in the same call then populate it. Without the flag a missing
  path is still an error.
- **Cold start without a human.** New `dynamo_op` op `launch` posts Revit's
  `ID_VISUAL_PROGRAMMING_DYNAMO` command (the Manage > Dynamo button);
  `dynamo_run_graph` launches automatically when Dynamo is not running and
  polls until its model answers (measured: ~14 s). `dynamo_status
  {launch: true}` does the same on request.

### Add-in

- **The socket service starts with Revit.** `ApplicationInitialized` starts it,
  so an AI client can connect to a freshly started Revit with nobody clicking
  the ribbon. `settings.autoStart: false` in `commandRegistry.json` or
  `REVIT_MCP_AUTOSTART=0` keeps the old manual behaviour. The switch's dialogs
  now say which way they toggled.

### Tooling

- `tools/Deploy-Local.ps1` copies the server build to the AppData copy the AI
  client launches and the add-in to the Revit Addins folder (refusing while
  Revit runs), optionally building first. `server/TOOL-CONVENTIONS.md`
  documents the rules for tool files; `build/utils/selfTest.js` checks the
  reply helpers and catalogue and `Verify.ps1` runs it.
- Command-line C# builds should pass `-p:PublishAddinFiles=false`; the
  Nice3point SDK otherwise publishes a second copy of the command set into the
  Addins folder.
- **Release workflow.** The tag-triggered workflow now also attaches
  `mcp-servers-for-revit-<version>-server.zip` (the built server with runtime
  `node_modules`) so a per-year ZIP install has a server that matches it,
  instead of `npx` resolving to whatever the upstream project last published.
  The `npm-publish` job is disabled on this fork (`if: false`, with re-enable
  notes), since the npm package name and its trusted-publishing grant belong
  to the upstream repository. The per-year ZIPs stay: the installer is large,
  and people who install by hand prefer the ZIP for their own Revit year.
- `Make-Installer.ps1` passed its arguments to `Package.ps1` as a positional
  array, which put "Release" into `-Years` and stopped before any Setup.exe
  existed. It splats a hashtable now.
- `Verify.ps1` and `Make-Installer.ps1` find Inno Setup 7 as well as 6; the
  `Set-RevitMcpTarget` self-test no longer requires PowerShell 7.
  `Package.ps1` exits 0 explicitly, so `Make-Installer.ps1` no longer inherits
  the exit code of the non-fatal devDependency restore that runs last.
- A `.gitattributes` fixes line endings: LF in the repository, CRLF on
  checkout only for the Windows-only files (`.ps1`, `.iss`, `.sln`, `.cmd`),
  `.dyn`/`.dyf` never rewritten. Run `git add --renormalize .` once after
  pulling it.

### Install and docs (fork identity)

- **The fork now says it is the fork.** `READ ME FIRST.txt` (offered by the
  installer's finish page and dropped beside the server), the installer's
  finish page, its Start-menu link and support URLs, the add-in manifest's
  vendor link, `server/package.json` and both READMEs point at
  [MrGezz/mcp-servers-for-revit](https://github.com/MrGezz/mcp-servers-for-revit)
  and describe this fork's install: the bundled server registered by path,
  the socket that starts with Revit, the lean tool profile. The original
  project stays credited, but `npx -y mcp-server-for-revit` (which resolves to
  the 26-tool 1.0.0 on npm) no longer appears as the way to run this add-in's
  server.
- **"Register the MCP server" now reaches Claude Code.** `Set-RevitMcpTarget.ps1
  -IncludeClaudeCode` wrote the entry into `~\.claude\settings.json`, a file
  Claude Code does not read for MCP servers (user-scope servers live in
  `~\.claude.json`), so the installer's task configured Claude Desktop only.
  The script now registers through Claude Code's own CLI (`claude mcp add
  --scope user`), which is also the only safe way to touch that file: Windows
  PowerShell 5.1's JSON round-trip rewrites the ISO timestamps it holds.
  Measured in a sandboxed `CLAUDE_CONFIG_DIR`: the entry lands as
  `{type: stdio, command: node, args: [...index.js]}`.
- **Node.js floor is 20 everywhere.** `server/package.json` has required
  `>=20` since the upstream change that dropped `better-sqlite3`, while the
  README, the installer's check and the release workflow said 18 (end-of-life
  since April 2025). Docs, the installer's warning threshold and CI now agree
  on 20.
- `scripts/release.ps1` checked out `main` and hard-reset it before bumping
  the version; this fork releases from `features/icz-addin`. It now tags the
  branch that is checked out and refuses a dirty tree instead of discarding it.
- **The binaries say who built them.** `RevitMCPPlugin.dll` carried the
  upstream author's name as Company and a 2025 copyright in its version
  resource, and `RevitMCPCommandSet.dll` shipped as 1.0.0.0 with the assembly
  name as Company because nothing versioned it. Both DLLs and the Setup.exe now
  report MrGezz, the release version, and the MIT notice; `scripts/release.ps1`
  bumps the command set's `<Version>` too.
- README: a fresh install needs no visit to Settings. The add-in has seeded
  `commandRegistry.json` with every deployed command enabled on first start
  since 1.0.1, and reconciles newly deployed commands into it after an
  upgrade; the ZIP steps said the opposite. The Revit 2027 / .NET 10
  configuration is listed under Development, and the release steps name the
  fork's branch. Every row of the Supported Tools tables now shows the
  `revit_tools` group that enables the tool, since the purpose-based
  headings and the groups do not always coincide.

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
  test and nothing more (measured 2026-08-31). The `-p:RevitTestTfm=` override
  you need to run the tests against Revit 2026.0–2026.2 rather than 2026.5 is
  documented in `tests/commandset/RevitMCPCommandSet.Tests.csproj`.
- The inherited defect list from the upstream plans was worked through
  (`tools/backlog-audit.mjs`), including two claims that were **tested and
  refuted** — acting on either would have broken working code.

---

## v1.0.0

Initial release. Revit 2020–2026, 26 MCP tools, per-version release ZIPs.
