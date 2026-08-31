import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateReferencePlaneTool(server: McpServer) {
  server.tool(
    "create_reference_plane",
    "Create reference planes in the Revit model. Supports reference planes defined by start/end points, normal vector, and view. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Start point of the reference plane in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("End point of the reference plane in mm"),
        normal: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe("Normal vector of the reference plane"),
        viewName: z.string().optional().describe("Name of the view in which the reference plane is visible"),
      })).describe("Array of reference planes to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_reference_plane", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create reference plane failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
