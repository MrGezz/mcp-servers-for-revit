[![Cover Image](./assets/cover.png?v=2)](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit)

# mcp-servers-for-revit

**Connect AI assistants to Autodesk Revit via the Model Context Protocol.**

mcp-servers-for-revit enables AI clients like Claude, Cline, and other MCP-compatible tools to read, create, modify, and delete elements in Revit projects. It consists of three components: a TypeScript MCP server that exposes tools to AI, a C# Revit add-in that bridges commands into Revit, and a command set that implements the actual Revit API operations.

> [!NOTE]
> This is a fork of the original [revit-mcp](https://github.com/mcp-servers-for-revit/revit-mcp) project with additional tools and functionality improvements.

## Architecture

```mermaid
flowchart LR
    Client["MCP Client<br/>(Claude, Cline, etc.)"]
    Server["MCP Server<br/><code>server/</code>"]
    Plugin["Revit Plugin<br/><code>plugin/</code>"]
    CommandSet["Command Set<br/><code>commandset/</code>"]
    Revit["Revit API"]

    Client <-->|stdio| Server
    Server <-->|WebSocket| Plugin
    Plugin -->|loads| CommandSet
    CommandSet -->|executes| Revit
```

The **MCP Server** (TypeScript) translates tool calls from AI clients into WebSocket messages. The **Revit Plugin** (C#) runs inside Revit, listens for those messages, and dispatches them to the **Command Set** (C#), which executes the actual Revit API operations and returns results back up the chain.

## Requirements

- **Node.js 18+** (for the MCP server)
- **Autodesk Revit 2020 - 2027** (any supported version)

## Quick Start (Installer)

Download `mcp-servers-for-revit-<version>-Setup.exe` from the
[Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) page and run it.

It ticks the Revit versions it finds on the machine, refuses to run while Revit
is open (the add-in DLLs are loaded and cannot be replaced), and installs to
`%AppData%\Autodesk\Revit\Addins\<year>\` with no administrator rights.

**Use the installer rather than the ZIP if you can.** Windows marks files
extracted from a downloaded archive, and the .NET loader then refuses the DLL
with `FileLoadException ... HRESULT 0x80131515`, which Revit reports only as
"cannot run the external application". Measured on Windows 11:

| How the payload arrives | Mark left on the DLL |
| --- | --- |
| ZIP, extracted with Explorer's **Extract All** | `ZoneId=3` — the add-in fails to load |
| ZIP, extracted with PowerShell `Expand-Archive` | none |
| **Setup.exe** | none |

That difference is why the failure looks intermittent between users. The
installer avoids it entirely; if you use the ZIP, unblock it *before* extracting.

Then register the MCP server with your AI client — see
[MCP Server Setup](#mcp-server-setup). The Revit add-in is only half of it.

## Quick Start (Using a Release ZIP)

1. Download the ZIP for your Revit version from the [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) page (e.g., `mcp-servers-for-revit-v1.0.0-Revit2025.zip`)

2. Extract the ZIP and copy the contents to your Revit addins folder:
   ```
   %AppData%\Autodesk\Revit\Addins\<your Revit version>\
   ```
   After copying you should have:
   ```
   Addins/2025/
   ├── mcp-servers-for-revit.addin
   └── revit_mcp_plugin/
       ├── RevitMCPPlugin.dll
       ├── ...
       └── Commands/
           └── RevitMCPCommandSet/
               ├── command.json
               └── 2025/
                   ├── RevitMCPCommandSet.dll
                   └── ...
   ```

3. Configure the MCP server in your AI client (see [MCP Server Setup](#mcp-server-setup))

4. Start Revit — if prompted about an unknown add-in, click **Always Load**

5. In Revit, click the **Settings** button on the mcp-servers-for-revit ribbon tab, enable the commands you want to use, and click **Save**

   Do this **before** step 6. This dialog is what writes the command registry.
   A server opened before it has been saved registers **no commands at all** and
   still reports itself as running — every tool then answers
   `Method '...' not found`.

6. Click **Revit MCP Switch** on the same ribbon tab. A dialog confirms *Open Server*.

   The switch is a toggle, and commands are bound when the server *opens*. If you
   change which commands are enabled, switch it off and on again to reload them.

7. Check it from your AI client by asking it to call `get_current_view_info`. It
   should return the active view.

   | What you see | What it means |
   | --- | --- |
   | The active view is returned | Working |
   | `Method '...' not found` | Step 5 was skipped, or step 6 ran before it — save in Settings, then toggle the switch off and on |
   | Connection refused / `ECONNREFUSED` | The switch is off, or Revit is not running |

## MCP Server Setup

The MCP server is published as an npm package and can be run directly with `npx`.

**Claude Code**

Run this in a **terminal** (not inside Claude Code):

```bash
claude mcp add --scope user mcp-server-for-revit -- cmd /c npx -y mcp-server-for-revit
```

`--scope user` registers the server for every project. Without it the scope
defaults to `local`, which registers it only for the directory the command was
run in — which is why the tools sometimes appear in one project and are missing
in the next.

**If `claude` is not found, the CLI is not on your `PATH`.** Installed and
on the `PATH` are not the same thing, so check rather than assume:

```bash
claude --version
```

If that fails you have two options. Either call the executable by its full path —
the Windows native install puts it at
`%AppData%\Claude\claude-code\<version>\claude.exe` — or skip the CLI and add
the entry by hand to `~/.claude/settings.json`
(`%UserProfile%\.claude\settings.json` on Windows):

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "cmd",
            "args": ["/c", "npx", "-y", "mcp-server-for-revit"]
        }
    }
}
```

Merge that into the existing `mcpServers` object rather than replacing the file,
and restart Claude Code afterwards.

**On macOS and Linux, drop the `cmd /c`** and use `-- npx -y mcp-server-for-revit`.
The wrapper is needed only on Windows, where `npx` is a `.cmd` shim and cannot be
launched directly as a process.

**Claude Desktop**

Claude Desktop → Settings → Developer → Edit Config → `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "cmd",
            "args": ["/c", "npx", "-y", "mcp-server-for-revit"]
        }
    }
}
```

Restart Claude Desktop. When you see the hammer icon, the MCP server is connected.

![Claude Desktop connection](./assets/claude.png)

## Revit Plugin Setup

If using a release ZIP, the plugin is already included. For manual installation:

1. Build the plugin from `plugin/` (see [Development](#development))
2. Copy `mcp-servers-for-revit.addin` to `%AppData%\Autodesk\Revit\Addins\<version>\`
3. Copy the `revit_mcp_plugin/` folder to the same addins directory

## Command Set Setup

If using a release ZIP, the command set is pre-installed inside the plugin. For manual installation:

1. Build the command set from `commandset/` (see [Development](#development))
2. Inside the plugin's installation directory, create `Commands/RevitMCPCommandSet/<year>/`
3. Copy the built DLLs into that folder
4. Copy `command.json` (from repo root) into `Commands/RevitMCPCommandSet/`

## Supported Tools

### General

| Tool | Description |
| ---- | ----------- |
| `say_hello` | Display a greeting dialog in Revit (connection test) |
| `send_code_to_revit` | Send C# code to Revit to execute via Roslyn |

### Query & Selection

| Tool | Description |
| ---- | ----------- |
| `get_current_view_info` | Get current active view info (name, type, scale, detail level) |
| `get_current_view_elements` | Get elements from the current active view |
| `get_selected_elements` | Get currently selected elements |
| `get_available_family_types` | Get available family types in current project |
| `ai_element_filter` | Intelligent element querying tool with multiple filter criteria |
| `query_parameters` | Get all parameters of an element with name, value, and storage type |
| `query_geometry` | Get geometry of an element including bounding box, solids, and faces |
| `query_references` | Get stable geometric references for dimensioning and tagging |
| `check_interferences` | Check interference collisions between specified elements |
| `query_view_range` | Get the view range of a plan view |

### Create — Architecture

| Tool | Description |
| ---- | ----------- |
| `create_wall` | Create walls with start/end points, height, thickness, and type |
| `create_floor` | Create floors with boundary polygon, thickness, and level |
| `create_ceiling` | Create ceilings with boundary, level, and thickness |
| `create_roof` | Create roofs with type (flat/pitched), boundary, and slope |
| `create_column` | Create architectural or structural columns at specified locations |
| `create_stair` | Create stairs with base/top level, width, riser, tread, and landings |
| `create_ramp` | Create ramps with base/top level and width |
| `create_railing` | Create railings along a path with height and type |
| `create_opening` | Create openings in walls, floors, or shafts |
| `create_model_curve` | Create model lines between start and end points |
| `create_reference_plane` | Create reference planes with start/end and normal direction |
| `create_group` | Create a group from selected element IDs |
| `create_grid` | Create a grid system with smart spacing generation |
| `create_level` | Create levels at specified elevations |
| `create_room` | Create and place rooms at specified locations |
| `create_structural_framing_system` | Create a structural beam framing system |
| `create_line_based_element` | Create line-based elements (wall, beam, pipe) — generic |
| `create_point_based_element` | Create point-based elements (door, window, furniture) — generic |
| `create_surface_based_element` | Create surface-based elements (floor, ceiling, roof) — generic |

### Create — MEP

| Tool | Description |
| ---- | ----------- |
| `create_duct` | Create ducts with start/end points, width, height, and system type |
| `create_pipe` | Create pipes with start/end points, diameter, and system type |
| `create_conduit` | Create conduits with start/end points and diameter |
| `create_equipment` | Place MEP equipment at specified locations with rotation |
| `create_space` | Create MEP spaces at specified locations |
| `create_direct_shape` | Create primitive solid geometry (box, cylinder, extrusion) as DirectShape |
| `create_swept_shape` | Create swept solids along a path with section profiles |
| `create_mep_curve` | Create MEP curve elements (duct/pipe/conduit) — multi-type |
| `connect_mep` | Connect two MEP elements by their connectors |
| `create_mep_system` | Create MEP systems from selected elements |

### Annotation

| Tool | Description |
| ---- | ----------- |
| `create_dimensions` | Create dimension annotations between elements or points |
| `create_text_note` | Create text notes in views with content, position, and alignment |
| `create_tag` | Create independent tags for elements (doors, windows, walls, rooms) |
| `tag_all_walls` | Tag all walls in the current view |
| `tag_all_rooms` | Tag all rooms in the current view |
| `create_filled_region` | Create a filled region in a view with boundary points |
| `create_revision` | Create a revision record with name, date, and number |
| `create_revision_cloud` | Create a revision cloud in a view associated with a revision |

### Views & Sheets

| Tool | Description |
| ---- | ----------- |
| `create_view` | Create views (floor plan, ceiling plan, elevation, section, 3D) |
| `create_drafting_view` | Create a drafting view with specified name and scale |
| `create_section_view` | Create a section view with bounding box |
| `create_elevation_view` | Create an elevation view at a direction index |
| `create_callout` | Create a callout view from a host view |
| `duplicate_view` | Duplicate a view with duplicate, with detailing, or dependent mode |
| `create_view_template` | Create a view template from a source view |
| `create_sheet` | Create sheets with number, name, and optional title block |
| `place_view_on_sheet` | Place a view onto a sheet at a specified location |
| `create_schedule` | Create schedules (regular, material, keynote, view/sheet/revision list) |
| `place_schedule_on_sheet` | Place an existing schedule on a sheet |
| `create_detail_curve` | Create detail lines in a view |
| `set_view_properties` | Set view properties (scale, detail level, crop box, display style, template) |
| `set_category_overrides` | Set graphic overrides for a category in a view |
| `manage_view_filters` | Add or remove view filters with visibility and overrides |
| `set_view_range` | Set the plan view range offsets |
| `manage_schedule_fields` | Add, remove, reorder, or hide schedule fields |
| `manage_graphics_resources` | Manage line styles and fill patterns |

### Modify

| Tool | Description |
| ---- | ----------- |
| `operate_element` | Operate on elements (select, setColor, hide, isolate, etc.) |
| `color_elements` | Color elements based on a parameter value |
| `delete_element` | Delete elements by ID |
| `set_parameters` | Batch set parameters on elements with key-value pairs |
| `transform_elements` | Move, copy, rotate, or mirror elements |
| `rename_element` | Rename a Revit element (level, grid, view, type) |
| `set_element_curve` | Modify location curve of linear elements |
| `duplicate_type` | Duplicate an element type with a new name |
| `manage_family_parameters` | Add, rename, remove, or set formulas on family parameters |
| `manage_project_parameters` | List or add shared parameters to the project |

### Family

| Tool | Description |
| ---- | ----------- |
| `load_family` | Load a family .rfa file into the current project |
| `place_family_instance` | Place family instances (unhosted, hosted, face-based, workplane-based) |

### Analysis & Data

| Tool | Description |
| ---- | ----------- |
| `analyze_model_statistics` | Analyze model complexity with element counts by category, type, family, and level |
| `export_room_data` | Export all room data from the project |
| `get_material_quantities` | Calculate material quantities and takeoffs |
| `export_views` | Export views to files (PNG, JPG, DWG, DXF, IFC) |

### Document

| Tool | Description |
| ---- | ----------- |
| `save_document` | Save the current Revit document |

### Stored project data

| Tool | Description |
| ---- | ----------- |
| `store_project_data` | Store project metadata in the open model |
| `store_room_data` | Store room metadata in the open model, linked to its project |
| `query_stored_data` | Query stored project and room data |

These three no longer use a local SQLite file. They write through the project
memory layer below, into the Revit document itself. See **Memory** for why.

### Memory

Two layers, both package-wide.

**Knowledge memory** is durable and user-scoped: node chains, API recipes, tool
behaviour, team conventions, and anything bulk-loaded from a document. It answers
"have we worked this out before?" so a session does not research it again. It needs
no Revit connection.

| Tool | Description |
| ---- | ----------- |
| `knowledge_search` | Search stored knowledge across every namespace, with the matched terms returned |
| `knowledge_get` | Read one knowledge unit in full |
| `knowledge_add` | Store something worth not re-deriving, with its failure mode |
| `knowledge_ingest` | Bulk-load a `.md`, `.txt`, `.json` or `.csv` reference document into searchable units |
| `knowledge_stats` | What the store holds and where it lives on disk |

**Project memory** is model-scoped: an entity/relation graph written INTO the Revit
document through Extensible Storage, so it travels with the file through Save As,
worksharing and transmittal instead of desynchronising from it.

| Tool | Description |
| ---- | ----------- |
| `project_memory_write` | Record entities and relations about this model |
| `project_memory_query` | Search them, reporting how many were searched as well as how many matched |
| `project_memory_stats` | Counts by kind, and where the graph is stored |
| `project_memory_clear` | Remove the graph from the model (requires `confirm: true`) |

The older `store_project_data`, `store_room_data` and `query_stored_data` still work
and now write through this layer. They previously used a SQLite file resolved
relative to the package directory, which under `npx -y` lives inside the npm cache -
so data described as persisted was sitting in storage npm may clear without warning.

### Dynamo

Read and write Dynamo graphs, and drive Dynamo inside a running Revit.

The first three work entirely from the `.dyn` file — **Revit does not need to be running**,
and Dynamo does not need to be installed.

| Tool | Description |
| ---- | ----------- |
| `dynamo_read_graph` | Explain a `.dyn`/`.dyf`: every node with what feeds it and what it feeds, Python and Code Block bodies, Player inputs, package dependencies, structural problems |
| `dynamo_list_graphs` | List the graphs under a folder, optionally with each one's name, description, node count and packages |
| `dynamo_edit_graph` | Apply edits to a graph: rewrite Python/Code Block bodies, rewire nodes, change input values, add or remove nodes, rename and reposition |
| `dynamo_status` | Report whether Dynamo can be driven in the running Revit, and which graph is open |
| `dynamo_run_graph` | Open a graph in Dynamo and, with `confirm: true`, run it |

**Editing preserves the file.** A graph is mutated in place and written back with everything
the editor did not touch left exactly as it was — including the `View` block that holds the
canvas layout, and including numeric literals, so a one-value change produces a one-line diff
rather than a few hundred. This is verified against a corpus of real graphs by
`server/src/dynamo/selfTest.ts`:

```bash
node build/dynamo/selfTest.js /path/to/a/folder/of/graphs
```

**Running a graph cannot be undone.** Every other write in this project can be wrapped in a
Revit transaction and rolled back. A Dynamo graph opens and commits its own transactions, so
there is nothing to roll back into — `dynamo_run_graph` requires `confirm: true` rather than
offering a dry run that could not work.

#### Live backends

`dynamo_status` and `dynamo_run_graph` need a live Revit session. By default they use
this project's own plugin socket — nothing else is required.

| Backend | Transport | Needs |
| ------- | --------- | ----- |
| `native` (default) | this project's plugin socket, via the `dynamo_op` command | the add-in loaded and the MCP Switch on |
| `http` (optional) | `POST` to a URL you configure | `REVIT_MCP_DYNAMO_HTTP_URL` set to your own bridge |

The `http` backend is an escape hatch for sites that already run their own bridge into
Revit. It is off unless you set the URL, and this project ships no such endpoint.
The contract is deliberately tiny:

```http
POST <REVIT_MCP_DYNAMO_HTTP_URL>
Content-Type: application/json

{ "op": "status" | "open" | "run", ... }
```

answered with `{ "ok": true, ... }` or `{ "ok": false, "message": "..." }`. The verb
travels in `op` rather than the URL path, which is what lets either backend serve the
same tools.

`REVIT_MCP_DYNAMO_BACKEND` is `auto` by default — use the HTTP bridge if one is
configured, otherwise native. Set it to `native` or `http` to pin one and get that
backend's error directly instead of a summary of both.
| `say_hello` | Display a greeting dialog in Revit (connection test) |

## Testing

The test project uses [Nice3point.TUnit.Revit](https://github.com/Nice3point/RevitUnit) to run integration tests against a live Revit instance. No separate addin installation is required — the framework injects into the running Revit process automatically.

### Prerequisites

- **.NET 10 SDK** — required by Nice3point.Revit.Sdk 6.1.0. Install via `winget install Microsoft.DotNet.SDK.10`
- **Autodesk Revit 2026** (or 2025) — must be installed and licensed on your machine

### Running Tests

1. Open Revit 2026 (or 2025) and wait for it to fully load
2. Run the tests from the command line:

```bash
# For Revit 2026
dotnet test -c Debug.R26 -r win-x64 tests/commandset

# For Revit 2025
dotnet test -c Debug.R25 -r win-x64 tests/commandset
```

> **Note:** The `-r win-x64` flag is required on ARM64 machines because the Revit API assemblies are x64-only.

Alternatively, you can use `dotnet run`:

```bash
cd tests/commandset
dotnet run -c Debug.R26
```

### IDE Support

- **JetBrains Rider** — enable "Testing Platform support" in Settings > Build, Execution, Deployment > Unit Testing > Testing Platform
- **Visual Studio** — tests should be discoverable through the standard Test Explorer

### Test Structure

| Directory | Purpose |
|-----------|---------|
| `tests/commandset/AssemblyInfo.cs` | Global `[assembly: TestExecutor<RevitThreadExecutor>]` registration |
| `tests/commandset/Architecture/` | Tests for level and room creation commands |
| `tests/commandset/DataExtraction/` | Tests for model statistics, room data export, and material quantities |
| `tests/commandset/ColorSplashTests.cs` | Tests for color override functionality |
| `tests/commandset/TagRoomsTests.cs` | Tests for room tagging functionality |

### Writing New Tests

Test classes inherit from `RevitApiTest` and use TUnit's async assertion API:

```csharp
public class MyTests : RevitApiTest
{
    private static Document _doc;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task MyTest_Condition_ExpectedResult()
    {
        var elements = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .ToElements();

        await Assert.That(elements.Count).IsGreaterThan(0);
    }
}
```

## Building a release

```powershell
# Per-Revit-version folders and ZIPs in dist\
powershell -ExecutionPolicy Bypass -File .\tools\Package.ps1

# ...and a single Setup.exe wrapping all of them
powershell -ExecutionPolicy Bypass -File .\tools\Make-Installer.ps1

# The gate: year declarations agree, every configuration compiles, every
# payload has the shape Revit needs, the server builds, ISCC accepts the script
powershell -ExecutionPolicy Bypass -File .\tools\Verify.ps1
```

`Make-Installer.ps1` needs Inno Setup (`winget install JRSoftware.InnoSetup`);
`Package.ps1` needs only the .NET SDK. The resulting `Setup.exe` is unsigned, so
SmartScreen warns until the file earns reputation.

The supported Revit versions are declared in seven places (two `.csproj`, the
`.sln`, `release.yml`, `Package.ps1`, and the `.iss` twice). Nothing makes them
agree, so `Verify.ps1` asserts that they do — add a year and it tells you which
file you forgot.

## Development

### MCP Server

```bash
cd server
npm install
npm run build
```

The server compiles TypeScript to `server/build/`. During development you can run it directly with `npx tsx server/src/index.ts`.

### Revit Plugin + Command Set

Open `mcp-servers-for-revit.sln` in Visual Studio. The solution contains both the plugin and command set projects. Build configurations target Revit 2020-2026:

- **Revit 2020-2024**: .NET Framework 4.8 (`Release R20` through `Release R24`)
- **Revit 2025-2026**: .NET 8 (`Release R25`, `Release R26`)

Building the solution automatically assembles the complete deployable layout in `plugin/bin/AddIn <year> <config>/` - the command set is copied into the plugin's `Commands/` folder as part of the build.

## Project Structure

```
mcp-servers-for-revit/
├── mcp-servers-for-revit.sln    # Combined solution (plugin + commandset + tests)
├── command.json     # Command set manifest
├── server/          # MCP server (TypeScript) - tools exposed to AI clients
├── plugin/          # Revit add-in (C#) - WebSocket bridge inside Revit
├── commandset/      # Command implementations (C#) - Revit API operations
├── tests/           # Integration tests (C#) - TUnit tests against live Revit
├── assets/          # Images for documentation
├── .github/         # CI/CD workflows, contributing guide, code of conduct
├── LICENSE
└── README.md
```

## Releasing

A single `v*` tag drives the entire release. The [release workflow](.github/workflows/release.yml) automatically:

- Builds the Revit plugin + command set for Revit 2020-2026
- Creates a GitHub release with `mcp-servers-for-revit-vX.Y.Z-Revit<year>.zip` assets
- Publishes the MCP server to npm as [`mcp-server-for-revit`](https://www.npmjs.com/package/mcp-server-for-revit)

To create a release:

1. Run the bump script (updates `server/package.json`, `server/package-lock.json`, and `plugin/Properties/AssemblyInfo.cs`, then commits and tags):
   ```powershell
   ./scripts/release.ps1 -Version X.Y.Z
   ```

2. Push to trigger the workflow:
   ```bash
   git push origin main --tags
   ```

> [!NOTE]
> npm publish uses [trusted publishing](https://docs.npmjs.com/trusted-publishers/) via OIDC — no npm token is required. Provenance attestation is generated automatically.

## Acknowledgements

This project is a fork of the work by the [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit) team. The original repositories:

- [revit-mcp](https://github.com/mcp-servers-for-revit/revit-mcp) - MCP server
- [revit-mcp-plugin](https://github.com/mcp-servers-for-revit/revit-mcp-plugin) - Revit plugin
- [revit-mcp-commandset](https://github.com/mcp-servers-for-revit/revit-mcp-commandset) - Command set

Thank you to the original authors for creating the foundation that this project builds upon.

## License

[MIT](LICENSE)
