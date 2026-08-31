import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateModelCurveTool(server: McpServer) {
  server.tool(
    "create_model_curve",
    "Create model curves in the Revit model. Supports lines and arcs with start/end points, curve type, and sketch plane level. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Start point in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("End point in mm"),
        curveType: z.string().optional().describe("Curve type: Line or Arc"),
        sketchPlaneLevel: z.number().optional().describe("Sketch plane level elevation in mm"),
      })).describe("Array of model curve definitions to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_model_curve", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create model curve failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
