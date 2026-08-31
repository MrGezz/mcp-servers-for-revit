import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSetViewPropertiesTool(server: McpServer) {
  server.tool(
    "set_view_properties",
    "Set view properties in Revit including scale, detail level, crop box, display style, and view template. All units in mm.",
    {
      viewId: z.number().int().describe("View ID to modify"),
      properties: z.object({
        scale: z.number().int().optional().describe("View scale"),
        detailLevel: z.enum(["Coarse", "Medium", "Fine"]).optional().describe("Detail level"),
        displayStyle: z.enum(["wireframe", "hidden", "shaded", "consistent_colors", "realistic"]).optional().describe("Display style"),
        cropBox: z.object({
          minX: z.number(),
          minY: z.number(),
          maxX: z.number(),
          maxY: z.number(),
        }).optional().describe("Crop box bounds in mm"),
        templateId: z.number().int().optional().describe("View template ID to apply"),
      }).describe("View properties to set"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_view_properties", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Set view properties failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
