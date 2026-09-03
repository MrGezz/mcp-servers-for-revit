import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { errorMessage, fail, fromRevit, ok } from "../utils/reply.js";
import { Limit } from "../utils/schemas.js";

type ElementRow = { Id?: number; Properties?: Record<string, string> };

export function registerGetSelectedElementsTool(server: McpServer) {
  server.tool(
    "get_selected_elements",
    "Elements currently selected in the Revit UI: id, name, category and parameters, up to `limit` records (default 30). Says explicitly when nothing is selected.",
    {
      limit: Limit(30),
    },
    async (args) => {
      try {
        const response = (await withRevitConnection(async (client) =>
          client.sendCommand("get_selected_elements", { limit: args.limit ?? 30 })
        )) as ElementRow[] | null;

        if (Array.isArray(response) && response.length === 0) {
          // An empty array on its own reads like "no data"; a small model then
          // guesses. Say what it means and what to do instead.
          return ok({
            ok: true,
            selected: [],
            note: "Nothing is selected in Revit. Ask the user to select elements, or find them with get_current_view_elements / ai_element_filter.",
          });
        }
        for (const row of Array.isArray(response) ? response : []) {
          if (row.Properties && row.Properties.ElementId === String(row.Id)) delete row.Properties.ElementId;
        }
        return fromRevit(response, "get_selected_elements");
      } catch (error) {
        return fail(`get_selected_elements failed: ${errorMessage(error)}`);
      }
    }
  );
}
