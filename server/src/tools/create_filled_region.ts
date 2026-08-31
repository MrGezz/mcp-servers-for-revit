import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateFilledRegionTool(server: McpServer) {
  server.tool(
    "create_filled_region",
    "Create a filled region in a Revit view with boundary points. All coordinates in mm.",
    {
      viewId: z.number().int().describe("Target view ID"),
      boundary: z.array(z.array(z.object({
        x: z.number().describe("X coordinate in mm"),
        y: z.number().describe("Y coordinate in mm"),
      }))).describe("Boundary loops (array of point arrays)"),
      filledRegionTypeName: z.string().optional().describe("Filled region type name"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_filled_region", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create filled region failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
