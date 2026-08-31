import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateDirectShapeTool(server: McpServer) {
  server.tool(
    "create_direct_shape",
    "Create primitive solid geometry shapes (box, cylinder, extrusion) as DirectShape elements in Revit. All units in mm.",
    {
      data: z.array(z.object({
        shapeType: z.enum(["Box", "Cylinder", "Extrusion"]).describe("Shape type"),
        width: z.number().optional().describe("Width in mm (Box)"),
        depth: z.number().optional().describe("Depth in mm (Box)"),
        height: z.number().optional().describe("Height in mm (Box/Cylinder/Extrusion)"),
        radius: z.number().optional().describe("Radius in mm (Cylinder)"),
        center: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe("Center point in mm"),
        curveType: z.string().optional().describe("Curve type for extrusion profile (Line)"),
        points: z.array(z.object({ x: z.number(), y: z.number(), z: z.number() })).optional().describe("Profile points for extrusion in mm"),
        extrusionDir: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe("Extrusion direction vector"),
        extrusionLength: z.number().optional().describe("Extrusion length in mm"),
        category: z.string().optional().describe("Target category name"),
        material: z.string().optional().describe("Material name"),
        typeId: z.number().optional().describe("Type ID"),
      })).describe("Array of shapes to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_direct_shape", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create direct shape failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
