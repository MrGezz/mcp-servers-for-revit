import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerQueryGeometryTool(server: McpServer) {
  server.tool(
    "query_geometry",
    "Query geometry information of a Revit element. Returns bounding box, solid count, and face details.",
    {
      elementId: z.number().int().describe("The element ID to query geometry for"),
      viewId: z.number().int().optional().describe("Optional view ID for geometry computation"),
      detailLevel: z.number().int().optional().describe("Optional detail level (0=Coarse, 1=Medium, 2=Fine)"),
    },
    async (args, extra) => {
      const params: any = { elementId: args.elementId };
      if (args.viewId !== undefined) params.viewId = args.viewId;
      if (args.detailLevel !== undefined) params.detailLevel = args.detailLevel;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("query_geometry", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Query geometry failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
