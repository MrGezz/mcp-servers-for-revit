import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateCalloutTool(server: McpServer) {
  server.tool(
    "create_callout",
    "Create a callout view from a host view in Revit with bounding box. All units in mm.",
    {
      name: z.string().optional().describe("Callout view name"),
      hostViewId: z.number().int().describe("Host view ID"),
      boundingBox: z.object({
        minX: z.number().describe("Min X in mm"),
        minY: z.number().describe("Min Y in mm"),
        maxX: z.number().describe("Max X in mm"),
        maxY: z.number().describe("Max Y in mm"),
      }).describe("Callout bounding box"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_callout", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create callout failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
