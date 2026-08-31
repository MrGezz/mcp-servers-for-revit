import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerPlaceFamilyInstanceTool(server: McpServer) {
  server.tool(
    "place_family_instance",
    "Place a family instance by FamilySymbol ID with support for unhosted, hosted, face-based, and workplane placement. Optional rotation. All units in mm.",
    {
      data: z.array(z.object({
        symbolId: z.number().describe("FamilySymbol ID"),
        placementType: z.enum(["Unhosted", "Hosted", "FaceBased", "Workplane"]).describe("Placement type"),
        location: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Insertion point in mm"),
        hostId: z.number().optional().describe("Host element ID (required for Hosted placement type)"),
        level: z.number().optional().describe("Level elevation in mm"),
        rotation: z.number().optional().describe("Rotation in degrees around the Z-axis"),
      })).describe("Array of family instance definitions to place"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("place_family_instance", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Place family instance failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
