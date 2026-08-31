import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerQueryParametersTool(server: McpServer) {
  server.tool(
    "query_parameters",
    "Query all parameters of a Revit element by element ID. Returns parameter name, value, and storage type for each parameter.",
    {
      elementId: z.number().int().describe("The element ID to query parameters for"),
    },
    async (args, extra) => {
      const params = { elementId: args.elementId };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("query_parameters", params);
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
              text: `Query parameters failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
