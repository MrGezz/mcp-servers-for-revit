import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateEquipmentTool(server: McpServer) {
  server.tool(
    "create_equipment",
    "Create MEP equipment instances in the Revit model. Supports placement of mechanical, electrical, and plumbing equipment with location, rotation, and family type. All units in mm.",
    {
      data: z.array(z.object({
        location: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Equipment location in mm"),
        rotation: z.number().optional().describe("Rotation around Z-axis in degrees"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        baseOffset: z.number().optional().describe("Base offset in mm"),
        category: z.string().optional().describe("Equipment category (Mechanical Equipment, Electrical Equipment, etc.)"),
        equipmentType: z.string().optional().describe("Equipment type name"),
        familyName: z.string().optional().describe("Family name"),
        typeId: z.number().optional().describe("Family type ID"),
      })).describe("Array of equipment to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_equipment", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create equipment failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
