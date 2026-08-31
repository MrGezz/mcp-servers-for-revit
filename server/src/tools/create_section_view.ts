import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateSectionViewTool(server: McpServer) {
  server.tool(
    "create_section_view",
    "Create a section view in Revit with a bounding box and view family type. All units in millimetres.",
    {
      name: z.string().optional().describe("View name"),
      boundingBox: z.object({
        minX: z.number().optional().default(-50000).describe("Min X in mm"),
        minY: z.number().optional().default(-50000).describe("Min Y in mm"),
        minZ: z.number().optional().default(-50000).describe("Min Z in mm"),
        maxX: z.number().optional().default(50000).describe("Max X in mm"),
        maxY: z.number().optional().default(50000).describe("Max Y in mm"),
        maxZ: z.number().optional().default(50000).describe("Max Z in mm"),
      }).optional().describe("Section bounding box"),
      viewFamilyTypeName: z.string().optional().default("Section").describe("View family type name"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_section_view", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create section view failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
