/**
 * Tool groups, profiles, and the `revit_tools` meta-tool.
 *
 * WHY. With every tool registered, the catalogue an MCP client sends to the
 * model measured 101 tools / ~115 KB of JSON / roughly 32,000 tokens, and it is
 * re-sent on every request of every turn. On a free-tier client that is most of
 * the budget before a single word of the conversation. Yet a typical session
 * touches five or six tools.
 *
 * So the server now registers everything but ENABLES only a profile. The
 * `revit_tools` tool lists the rest as one line per group and enables a group on
 * demand; the SDK then sends `notifications/tools/list_changed` and the client
 * re-reads the catalogue. Nothing was removed — REVIT_MCP_PROFILE=full is the
 * old behaviour.
 *
 *   REVIT_MCP_PROFILE   core (default) | standard | full
 *   REVIT_MCP_TOOLS     comma-separated groups to add at start-up, e.g. "views,mep"
 *                       (prefix "-" to drop one: "-memory")
 */
import { z } from "zod";
import type { McpServer, RegisteredTool } from "@modelcontextprotocol/sdk/server/mcp.js";
import { fail, ok } from "./utils/reply.js";

export interface ToolEntry {
  tool: RegisteredTool;
  group: string;
  description: string;
}

export type Registry = Map<string, ToolEntry>;

/** Every tool name belongs to exactly one group. Unknown names land in `other`. */
export const GROUPS: Record<string, { summary: string; tools: string[] }> = {
  core: {
    summary: "Always on: connection, current view, selection, element search, parameters, C# execution, delete, Dynamo run",
    tools: [
      "revit_tools",
      "say_hello",
      "get_current_view_info",
      "get_current_view_elements",
      "get_selected_elements",
      "ai_element_filter",
      "query_parameters",
      "set_parameters",
      "get_available_family_types",
      "send_code_to_revit",
      "delete_element",
      "operate_element",
      "dynamo_status",
      "dynamo_run_graph",
    ],
  },
  query: {
    summary: "Read-only analysis: geometry, references, view range, clashes, model statistics, material and room takeoffs",
    tools: [
      "query_geometry",
      "query_references",
      "query_view_range",
      "check_interferences",
      "analyze_model_statistics",
      "get_material_quantities",
      "export_room_data",
    ],
  },
  arch: {
    summary: "Create architecture/structure: walls, floors, ceilings, roofs, columns, stairs, railings, openings, rooms, spaces, levels, grids, beams, curves, groups, shapes, family instances",
    tools: [
      "create_wall",
      "create_floor",
      "create_ceiling",
      "create_roof",
      "create_column",
      "create_stair",
      "create_railing",
      "create_opening",
      "create_room",
      "create_space",
      "create_level",
      "create_grid",
      "create_structural_framing_system",
      "create_model_curve",
      "create_reference_plane",
      "create_group",
      "create_direct_shape",
      "create_swept_shape",
      "place_family_instance",
      "load_family",
      "create_point_based_element",
      "create_line_based_element",
      "create_surface_based_element",
      "create_ramp",
    ],
  },
  mep: {
    summary: "Create MEP: ducts, pipes, conduits, equipment, systems, connect elements",
    tools: ["create_duct", "create_pipe", "create_conduit", "create_mep_curve", "create_equipment", "create_mep_system", "connect_mep"],
  },
  views: {
    summary: "Views, sheets, schedules, templates, filters, overrides, colouring, export",
    tools: [
      "create_view",
      "create_drafting_view",
      "create_section_view",
      "create_elevation_view",
      "create_callout",
      "duplicate_view",
      "create_view_template",
      "set_view_properties",
      "set_view_range",
      "set_category_overrides",
      "manage_view_filters",
      "manage_graphics_resources",
      "create_sheet",
      "place_view_on_sheet",
      "create_schedule",
      "manage_schedule_fields",
      "place_schedule_on_sheet",
      "export_views",
      "color_elements",
    ],
  },
  annotate: {
    summary: "Dimensions, tags, text notes, detail lines, filled regions, revisions and clouds",
    tools: [
      "create_dimensions",
      "create_tag",
      "tag_all_walls",
      "tag_all_rooms",
      "create_text_note",
      "create_detail_curve",
      "create_filled_region",
      "create_revision",
      "create_revision_cloud",
    ],
  },
  modify: {
    summary: "Move/copy/rotate/mirror, rename, reshape curves, duplicate types, family and project parameters, save",
    tools: [
      "transform_elements",
      "rename_element",
      "set_element_curve",
      "duplicate_type",
      "manage_family_parameters",
      "manage_project_parameters",
      "save_document",
    ],
  },
  dynamo: {
    summary: "Dynamo graph files: list, read/explain, edit .dyn (running graphs is in core)",
    tools: ["dynamo_list_graphs", "dynamo_read_graph", "dynamo_edit_graph"],
  },
  memory: {
    summary: "Knowledge memory (per user) and project memory stored inside the model",
    tools: [
      "knowledge_search",
      "knowledge_get",
      "knowledge_add",
      "knowledge_ingest",
      "knowledge_stats",
      "project_memory_write",
      "project_memory_query",
      "project_memory_stats",
      "project_memory_clear",
      "store_project_data",
      "store_room_data",
      "query_stored_data",
    ],
  },
};

const PROFILES: Record<string, string[]> = {
  core: ["core"],
  standard: ["core", "query", "modify", "annotate"],
  full: Object.keys(GROUPS),
};

const groupIndex = new Map<string, string>();
for (const [group, def] of Object.entries(GROUPS)) for (const name of def.tools) groupIndex.set(name, group);

export function groupOf(toolName: string): string {
  return groupIndex.get(toolName) ?? "other";
}

export function activeProfile(): string {
  const raw = (process.env.REVIT_MCP_PROFILE || "core").trim().toLowerCase();
  return PROFILES[raw] ? raw : "core";
}

/** Groups enabled at start-up: the profile, plus/minus REVIT_MCP_TOOLS. */
export function startupGroups(): Set<string> {
  const enabled = new Set(PROFILES[activeProfile()]);
  for (const raw of (process.env.REVIT_MCP_TOOLS || "").split(",")) {
    const token = raw.trim().toLowerCase();
    if (!token) continue;
    if (token.startsWith("-")) enabled.delete(token.slice(1));
    else if (token.startsWith("+")) enabled.add(token.slice(1));
    else enabled.add(token);
  }
  enabled.add("core");
  return enabled;
}

/**
 * Disable every registered tool outside the start-up groups. Runs before the
 * transport is connected, so the SDK's list_changed notifications are no-ops.
 */
export function applyProfile(registry: Registry): { enabled: string[]; disabled: string[] } {
  const groups = startupGroups();
  const enabled: string[] = [];
  const disabled: string[] = [];
  for (const [name, entry] of registry) {
    const on = groups.has(entry.group) || (entry.group === "other" && activeProfile() === "full");
    if (on) enabled.push(name);
    else {
      entry.tool.disable();
      disabled.push(name);
    }
  }
  return { enabled, disabled };
}

function groupState(registry: Registry, group: string) {
  const names = [...registry].filter(([, e]) => e.group === group).map(([n]) => n);
  const on = names.filter((n) => registry.get(n)!.tool.enabled);
  return { names, on };
}

function shortDescription(description: string): string {
  const first = description.split(/(?<=\.)\s/)[0] ?? description;
  return first.length > 110 ? first.slice(0, 107) + "..." : first;
}

/** The meta-tool through which a model discovers and switches on the rest of the catalogue. */
export function registerRevitToolsTool(server: McpServer, registry: Registry): void {
  const groupNames = Object.keys(GROUPS).filter((g) => g !== "core");
  server.tool(
    "revit_tools",
    "Discover and switch on the rest of the Revit toolset. Only a lean core is loaded by default. " +
      "action=list shows every group with what it covers and whether it is on; action=enable loads groups " +
      `(${groupNames.join(", ")}) so their tools become callable; action=describe returns one tool's description.`,
    {
      action: z.enum(["list", "enable", "disable", "describe"]),
      groups: z.array(z.string()).optional().describe("Group names for enable/disable"),
      tools: z.array(z.string()).optional().describe("Tool names for describe"),
    },
    async (args) => {
      if (args.action === "list") {
        const rows = Object.entries(GROUPS).map(([group, def]) => {
          const { names, on } = groupState(registry, group);
          return { group, on: on.length === names.length ? true : on.length === 0 ? false : `${on.length}/${names.length}`, summary: def.summary, tools: names.join(", ") };
        });
        return ok({ profile: activeProfile(), groups: rows, note: "Call revit_tools {action:'enable', groups:[...]} to load a group. Tools appear in your tool list right after." });
      }

      if (args.action === "describe") {
        const wanted = args.tools ?? [];
        if (wanted.length === 0) return fail("describe needs tools:[names]");
        const found = wanted.map((n) => {
          const e = registry.get(n);
          return e ? { tool: n, group: e.group, enabled: e.tool.enabled, description: e.description } : { tool: n, error: "unknown tool" };
        });
        return ok({ tools: found });
      }

      const groups = (args.groups ?? []).map((g) => g.trim().toLowerCase());
      if (groups.length === 0) return fail(`${args.action} needs groups:[...]. Known groups: ${groupNames.join(", ")}`);
      const unknown = groups.filter((g) => !GROUPS[g]);
      if (unknown.length) return fail(`Unknown group(s): ${unknown.join(", ")}. Known: ${groupNames.join(", ")}`);
      if (args.action === "disable" && groups.includes("core")) return fail("The core group cannot be disabled.");

      const changed: Array<{ tool: string; description: string }> = [];
      // The SDK sends one list_changed notification per enable()/disable(); a
      // group is 20+ tools, and some clients re-read the whole catalogue per
      // notification. Silence it for the batch and notify once at the end.
      const silenced = server as unknown as { sendToolListChanged: () => void };
      const own = Object.prototype.hasOwnProperty.call(silenced, "sendToolListChanged");
      const previous = silenced.sendToolListChanged;
      silenced.sendToolListChanged = () => {};
      try {
        for (const group of groups) {
          for (const name of GROUPS[group].tools) {
            const entry = registry.get(name);
            if (!entry) continue;
            if (args.action === "enable" && !entry.tool.enabled) {
              entry.tool.enable();
              changed.push({ tool: name, description: shortDescription(entry.description) });
            } else if (args.action === "disable" && entry.tool.enabled) {
              entry.tool.disable();
              changed.push({ tool: name, description: "" });
            }
          }
        }
      } finally {
        if (own) silenced.sendToolListChanged = previous;
        else delete (silenced as Partial<typeof silenced>).sendToolListChanged;
      }
      if (changed.length) server.sendToolListChanged();
      if (args.action === "enable") {
        return ok({
          ok: true,
          enabled: changed,
          note: changed.length
            ? "These tools are callable now (your tool list was refreshed). If your client does not show them, restart it with REVIT_MCP_PROFILE=full."
            : "Already enabled.",
        });
      }
      return ok({ ok: true, disabled: changed.map((c) => c.tool) });
    }
  );
}
