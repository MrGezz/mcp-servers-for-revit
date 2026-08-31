import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateStairTool(server: McpServer) {
  server.tool(
    "create_stair",
    "Create stairs in the Revit model. Supports stairs with location, direction, levels, width, riser/tread parameters, landing, and type. All units in mm.",
    {
      data: z.array(z.object({
        location: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Stair start location in mm"),
        direction: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Stair direction vector"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        topLevel: z.number().describe("Top level elevation in mm"),
        width: z.number().describe("Stair width in mm"),
        riserHeight: z.number().optional().describe("Riser height in mm"),
        treadDepth: z.number().optional().describe("Tread depth in mm"),
        stepCount: z.number().optional().describe("Number of steps"),
        typeId: z.number().optional().describe("Stair type ID"),
        stairType: z.string().optional().describe("Stair type name"),
        material: z.string().optional().describe("Stair material"),
        hasLanding: z.boolean().optional().describe("Whether the stair has a landing"),
        landingWidth: z.number().optional().describe("Landing width in mm"),
        landingDepth: z.number().optional().describe("Landing depth in mm"),
      })).describe("Array of stairs to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_stair", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create stair failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
