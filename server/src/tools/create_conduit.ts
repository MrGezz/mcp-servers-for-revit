import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateConduitTool(server: McpServer) {
  server.tool(
    "create_conduit",
    "Create conduits in the Revit model. Supports conduits with start/end points, diameter, and level. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Start point in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("End point in mm"),
        diameter: z.number().describe("Conduit diameter in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        baseOffset: z.number().optional().describe("Base offset in mm"),
        conduitType: z.string().optional().describe("Conduit type name"),
        typeId: z.number().optional().describe("Conduit type ID"),
      })).describe("Array of conduits to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_conduit", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create conduit failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
