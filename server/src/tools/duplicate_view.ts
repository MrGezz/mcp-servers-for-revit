import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDuplicateViewTool(server: McpServer) {
  server.tool(
    "duplicate_view",
    "Duplicate a view in Revit with the specified duplication mode.",
    {
      viewId: z.number().int().describe("Source view ID to duplicate"),
      mode: z.enum(["duplicate", "with_detailing", "dependent"]).optional().default("duplicate").describe("Duplication mode"),
      newName: z.string().optional().describe("New name for the duplicated view (optional)"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("duplicate_view", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Duplicate view failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
