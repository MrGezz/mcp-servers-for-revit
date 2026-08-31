import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateCeilingTool(server: McpServer) {
  server.tool(
    "create_ceiling",
    "Create ceilings in the Revit model. Supports ceilings with boundary points, thickness, level, and type. All units in mm.",
    {
      data: z.array(z.object({
        boundaryPoints: z.array(z.object({ x: z.number(), y: z.number(), z: z.number() })).describe("Ceiling boundary points in mm"),
        level: z.number().describe("Base level elevation in mm"),
        thickness: z.number().optional().describe("Ceiling thickness in mm"),
        levelOffset: z.number().optional().describe("Level offset in mm"),
        typeId: z.number().optional().describe("Ceiling type ID"),
        ceilingType: z.string().optional().describe("Ceiling type name"),
        material: z.string().optional().describe("Ceiling material"),
      })).describe("Array of ceiling definitions to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_ceiling", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create ceiling failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
