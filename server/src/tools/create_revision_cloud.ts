import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateRevisionCloudTool(server: McpServer) {
  server.tool(
    "create_revision_cloud",
    "Create a revision cloud in a Revit view with boundary points. All coordinates in mm.",
    {
      revisionId: z.number().int().describe("Revision ID to associate with"),
      viewId: z.number().int().describe("View ID to place the cloud in"),
      points: z.array(z.object({
        x: z.number().describe("X coordinate in mm"),
        y: z.number().describe("Y coordinate in mm"),
      })).describe("Boundary points of the revision cloud in mm"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_revision_cloud", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create revision cloud failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
