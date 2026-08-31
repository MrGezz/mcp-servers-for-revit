import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateViewTemplateTool(server: McpServer) {
  server.tool(
    "create_view_template",
    "Create a view template from an existing view in Revit.",
    {
      sourceViewId: z.number().int().describe("Source view ID to create template from"),
      name: z.string().describe("Template name"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_view_template", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create view template failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
