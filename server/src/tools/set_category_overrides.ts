import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSetCategoryOverridesTool(server: McpServer) {
  server.tool(
    "set_category_overrides",
    "Set graphic overrides for a category in a specific Revit view.",
    {
      viewId: z.number().int().describe("View ID"),
      categoryId: z.number().int().describe("Category ID to override"),
      overrides: z.object({
        color: z.object({
          r: z.number().int().min(0).max(255),
          g: z.number().int().min(0).max(255),
          b: z.number().int().min(0).max(255),
        }).optional().describe("RGB color override"),
        lineWeight: z.number().int().optional().describe("Line weight override"),
        fillPattern: z.string().optional().describe("Fill pattern name"),
        halftone: z.boolean().optional().describe("Apply halftone rendering"),
        transparency: z.number().int().min(0).max(100).optional().describe("Transparency percentage (0-100)"),
      }).describe("Graphic override settings"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_category_overrides", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Set category overrides failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
