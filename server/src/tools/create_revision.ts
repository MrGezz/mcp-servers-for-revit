import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateRevisionTool(server: McpServer) {
  server.tool(
    "create_revision",
    "Create a revision in Revit, specifying name, date, number, and description.",
    {
      name: z.string().describe("Revision name or short description"),
      date: z.string().optional().describe("Revision date"),
      number: z.string().optional().describe("Revision number"),
      description: z.string().optional().describe("Additional description"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_revision", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create revision failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
