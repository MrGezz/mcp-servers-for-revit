import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateRailingTool(server: McpServer) {
  server.tool(
    "create_railing",
    "Create railings in the Revit model. Supports railings with start/end points, height, level, type, and material. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Railing start point (x, y, z) in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Railing end point (x, y, z) in mm"),
        height: z.number().optional().describe("Railing height in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        levelOffset: z.number().optional().describe("Level offset in mm"),
        typeId: z.number().optional().describe("Railing type ID"),
        railingType: z.string().optional().describe("Railing type name"),
        material: z.string().optional().describe("Railing material"),
      })).describe("Array of railings to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_railing", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create railing failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
