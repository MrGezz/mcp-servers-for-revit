import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerQueryViewRangeTool(server: McpServer) {
  server.tool(
    "query_view_range",
    "Get the view range of a plan view in Revit. Returns top, cut plane, bottom, and view depth levels with offsets.",
    {
      viewId: z.number().int().describe("ID of the plan view to query"),
    },
    async (args, extra) => {
      const params = { viewId: args.viewId };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("query_view_range", params);
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
              text: `Query view range failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
