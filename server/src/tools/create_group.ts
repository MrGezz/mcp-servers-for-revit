import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateGroupTool(server: McpServer) {
  server.tool(
    "create_group",
    "Create element groups in the Revit model. Groups selected elements by their IDs with a specified group name.",
    {
      data: z.array(z.object({
        elementIds: z.array(z.number()).describe("Element IDs to include in the group"),
        groupName: z.string().describe("Name for the new group"),
      })).describe("Array of groups to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_group", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create group failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
