import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateDraftingViewTool(server: McpServer) {
  server.tool(
    "create_drafting_view",
    "Create a drafting view in Revit with custom name, scale, and detail level.",
    {
      name: z.string().optional().describe("View name"),
      scale: z.number().int().optional().default(100).describe("View scale (e.g. 100 for 1:100)"),
      detailLevel: z.enum(["Coarse", "Medium", "Fine"]).optional().default("Coarse").describe("Detail level"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_drafting_view", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create drafting view failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
