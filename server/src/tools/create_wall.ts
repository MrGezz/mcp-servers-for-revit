import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateWallTool(server: McpServer) {
  server.tool(
    "create_wall",
    "Create walls in the Revit model. Specify start/end points, height, thickness, base level, and wall type. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Start point of the wall in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("End point of the wall in mm"),
        height: z.number().describe("Wall height in mm"),
        thickness: z.number().optional().describe("Wall thickness in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        baseOffset: z.number().optional().describe("Base offset from level in mm"),
        topConstraintType: z.number().optional().describe("Top constraint type (0=Unconstrained, 1=Up to level, 2=Unconnected height)"),
        topLevelId: z.number().optional().describe("Top level ID"),
        topOffset: z.number().optional().describe("Top offset in mm"),
        wallType: z.string().optional().describe("Wall type name"),
        typeId: z.number().optional().describe("Wall type ID"),
        material: z.string().optional().describe("Wall material"),
        isStructural: z.boolean().optional().describe("Whether the wall is structural"),
        flipped: z.boolean().optional().describe("Flip wall direction"),
      })).describe("Array of walls to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_wall", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create wall failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
