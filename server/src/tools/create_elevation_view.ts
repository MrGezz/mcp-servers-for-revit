import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateElevationViewTool(server: McpServer) {
  server.tool(
    "create_elevation_view",
    "Create an elevation view in Revit using an elevation marker. Direction index 0-3 maps to project north/south/east/west.",
    {
      name: z.string().optional().describe("View name"),
      directionIndex: z.number().int().min(0).max(3).optional().default(0).describe("Direction index (0=north, 1=south, 2=east, 3=west)"),
      viewFamilyTypeName: z.string().optional().default("Elevation").describe("View family type name"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_elevation_view", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create elevation view failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
