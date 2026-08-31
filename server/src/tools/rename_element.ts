import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerRenameElementTool(server: McpServer) {
  server.tool(
    "rename_element",
    "Rename a Revit element. Works on elements with an editable Name parameter, levels, grids, and element types.",
    {
      elementId: z.number().int().describe("ID of the element to rename"),
      newName: z.string().min(1).describe("New name for the element"),
    },
    async (args, extra) => {
      const params = {
        elementId: args.elementId,
        newName: args.newName,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("rename_element", params);
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
              text: `Rename element failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
