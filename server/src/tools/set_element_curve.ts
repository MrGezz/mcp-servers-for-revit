import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSetElementCurveTool(server: McpServer) {
  server.tool(
    "set_element_curve",
    "Modify the location curve of linear elements (walls, beams, pipes, ducts, etc.) by setting start and end points.",
    {
      elementId: z.number().int().describe("The element ID to modify the curve on"),
      startPoint: z.object({
        x: z.number().describe("Start X coordinate"),
        y: z.number().describe("Start Y coordinate"),
        z: z.number().describe("Start Z coordinate"),
      }).describe("Start point of the curve in feet"),
      endPoint: z.object({
        x: z.number().describe("End X coordinate"),
        y: z.number().describe("End Y coordinate"),
        z: z.number().describe("End Z coordinate"),
      }).describe("End point of the curve in feet"),
    },
    async (args, extra) => {
      const params = {
        elementId: args.elementId,
        startPoint: args.startPoint,
        endPoint: args.endPoint,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_element_curve", params);
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
              text: `Set element curve failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
