import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageViewFiltersTool(server: McpServer) {
  server.tool(
    "manage_view_filters",
    "Add or remove view filters in Revit and optionally set graphic overrides.",
    {
      viewId: z.number().int().describe("View ID to manage filters on"),
      action: z.enum(["add", "remove"]).describe("Action: add or remove"),
      filterName: z.string().describe("Filter name"),
      overrides: z.object({
        visible: z.boolean().optional().describe("Filter visibility"),
        color: z.object({
          r: z.number().int().min(0).max(255),
          g: z.number().int().min(0).max(255),
          b: z.number().int().min(0).max(255),
        }).optional().describe("Override color"),
        lineWeight: z.number().int().optional().describe("Override line weight"),
        fillPattern: z.string().optional().describe("Override fill pattern name"),
        halftone: z.boolean().optional().describe("Halftone"),
      }).optional().describe("Filter graphic overrides (for add action)"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_view_filters", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Manage view filters failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
