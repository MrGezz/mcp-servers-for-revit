import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateFloorTool(server: McpServer) {
  server.tool(
    "create_floor",
    "Create floors in the Revit model. Create floors with boundary points, thickness, level, and type. All units in mm.",
    {
      data: z.array(z.object({
        level: z.number().describe("Base level elevation in mm"),
        thickness: z.number().optional().describe("Floor thickness in mm"),
        height: z.number().optional().describe("Floor height in mm"),
        boundaryPoints: z.array(z.object({ x: z.number(), y: z.number(), z: z.number() })).describe("Floor boundary points in mm"),
        levelOffset: z.number().optional().describe("Level offset in mm"),
        typeId: z.number().optional().describe("Floor type ID"),
        floorType: z.string().optional().describe("Floor type name"),
        material: z.string().optional().describe("Floor material"),
        isStructural: z.boolean().optional().describe("Whether the floor is structural"),
        levelName: z.string().optional().describe("Level name"),
      })).describe("Array of floors to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_floor", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create floor failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
