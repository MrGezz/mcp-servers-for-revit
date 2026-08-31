import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateSweptShapeTool(server: McpServer) {
  server.tool(
    "create_swept_shape",
    "Create swept solid shapes along a path with configurable section profiles (rect, circle, horseshoe). All units in mm.",
    {
      data: z.array(z.object({
        sectionType: z.enum(["Rect", "Circle", "Horseshoe"]).describe("Section profile type"),
        width: z.number().optional().describe("Section width in mm (Rect/Horseshoe)"),
        height: z.number().optional().describe("Section height in mm (Rect/Horseshoe)"),
        radius: z.number().optional().describe("Section radius in mm (Circle)"),
        pathPoints: z.array(z.object({ x: z.number(), y: z.number(), z: z.number() })).describe("Sweep path points in mm"),
        category: z.string().optional().describe("Target category name"),
      })).describe("Array of swept shapes to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_swept_shape", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create swept shape failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
