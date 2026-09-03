#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { readFileSync } from "fs";
import { registerTools } from "./tools/register.js";

// The version is READ from package.json rather than duplicated here: a hardcoded
// copy was left at 1.0.0 after the package moved on, which is wrong in the one
// place a client can actually see it.
const pkg = JSON.parse(
  readFileSync(new URL("../package.json", import.meta.url), "utf8")
) as { version?: string };

/**
 * Operating rules handed to the client at initialize time. Most MCP clients put
 * this text into the model's system prompt, which makes it the cheapest place to
 * prevent the mistakes small models make with this server: invented element ids,
 * wrong units, treating an error text as success, not knowing that more tools
 * exist. Kept short on purpose — it is paid for on every request too.
 */
const INSTRUCTIONS = `Revit MCP server (Autodesk Revit via a local add-in). Rules:
1. Units: every length, coordinate and offset is in millimetres unless the parameter says otherwise. Element ids are integers.
2. Never invent ElementIds, type ids, level or view names, or category names. Read them first with get_current_view_info, get_current_view_elements, get_selected_elements, ai_element_filter, get_available_family_types or query_parameters. Category names are Revit BuiltInCategory names such as OST_Walls, OST_Doors.
3. Only a lean core of tools is loaded to save tokens. If the task needs something you cannot see (creating walls/floors/MEP, views, sheets, schedules, annotations, editing Dynamo files, memory), call revit_tools {action:"list"} then revit_tools {action:"enable", groups:[...]}; the new tools become callable immediately.
4. Results are compact JSON. isError or {"ok":false,...} means the action did NOT happen: read the error, fix the call, do not retry the same call. A "_truncated" field means more data exists: narrow the query instead of assuming you saw everything.
5. Ask for small results: keep limits at or below 30 unless the user needs more.
6. Model edits run inside Revit transactions and can be undone in Revit with Ctrl+Z. dynamo_run_graph is the exception: a graph commits its own transactions and cannot be rolled back by this server. Confirm deletes, graph runs and memory clears with the user unless already authorised.
7. If Revit is unreachable, tell the user to start Revit, open a document, and check the "Revit MCP Switch" button on the Add-Ins ribbon (the add-in starts the server automatically unless auto-start was disabled).`;

const server = new McpServer(
  { name: "mcp-server-for-revit", version: pkg.version ?? "0.0.0" },
  { instructions: INSTRUCTIONS }
);

async function main() {
  await registerTools(server);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Server start success");
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
