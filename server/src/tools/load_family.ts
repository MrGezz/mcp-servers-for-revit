import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerLoadFamilyTool(server: McpServer) {
  server.tool(
    "load_family",
    "Load a .rfa family file into the Revit project.",
    {
      filePath: z.string().describe("Full path to the .rfa family file"),
      familyName: z.string().optional().describe("Expected family name after loading (optional)"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("load_family", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Load family failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
