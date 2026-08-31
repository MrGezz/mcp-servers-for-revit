import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateRoofTool(server: McpServer) {
  server.tool(
    "create_roof",
    "Create roofs in the Revit model. Supports flat, gable, and hip roofs with level, thickness, slope, overhang, and material. All units in mm.",
    {
      data: z.array(z.object({
        type: z.string().describe("Roof type: Flat, Gable, or Hip"),
        level: z.number().describe("Roof elevation in mm"),
        height: z.number().optional().describe("Roof height for pitched roofs in mm"),
        thickness: z.number().optional().describe("Roof thickness in mm"),
        slope: z.number().optional().describe("Roof slope in degrees"),
        overhang: z.number().optional().describe("Roof overhang distance from walls in mm"),
        material: z.string().optional().describe("Roof material name"),
      })).describe("Array of roofs to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_roof", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create roof failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
