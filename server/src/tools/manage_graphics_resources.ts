import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageGraphicsResourcesTool(server: McpServer) {
  server.tool(
    "manage_graphics_resources",
    "Manage graphics resources in Revit: create or update line styles and fill patterns.",
    {
      action: z.enum(["line_style", "fill_pattern"]).describe("Resource type to manage: line_style or fill_pattern"),
      name: z.string().describe("Name of the resource to create or update"),
      properties: z.object({
        color: z.object({
          r: z.number().int().min(0).max(255),
          g: z.number().int().min(0).max(255),
          b: z.number().int().min(0).max(255),
        }).optional().describe("RGB color"),
        lineWeight: z.number().int().optional().describe("Line weight, applicable to line_style resources"),
        linePattern: z.string().optional().describe("Line pattern name, applicable to line_style resources"),
      }).optional().describe("Properties to apply to the resource"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_graphics_resources", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Manage graphics resources failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
