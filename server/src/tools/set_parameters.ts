import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSetParametersTool(server: McpServer) {
  server.tool(
    "set_parameters",
    "Batch-set parameters on a Revit element. Provide the element ID and a key-value map of parameter names to values.",
    {
      elementId: z.number().int().describe("The element ID to set parameters on"),
      parameters: z.record(z.union([z.string(), z.number(), z.boolean()])).describe("Key-value pairs of parameter names and values (e.g. { \"Height\": 3000, \"Comment\": \"new\" })"),
    },
    async (args, extra) => {
      const params = {
        elementId: args.elementId,
        parameters: args.parameters,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_parameters", params);
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
              text: `Set parameters failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
