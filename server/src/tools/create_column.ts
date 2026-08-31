import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateColumnTool(server: McpServer) {
  server.tool(
    "create_column",
    "Create columns in the Revit model. Supports structural columns with location, dimensions, levels, type, and material. All units in mm.",
    {
      data: z.array(z.object({
        location: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Column location in mm"),
        height: z.number().describe("Column height in mm"),
        width: z.number().optional().describe("Column width in mm"),
        depth: z.number().optional().describe("Column depth in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        topLevel: z.number().optional().describe("Top level elevation in mm"),
        typeId: z.number().optional().describe("Column type ID"),
        columnType: z.string().optional().describe("Column type name"),
        material: z.string().optional().describe("Column material"),
        isStructural: z.boolean().optional().describe("Whether the column is structural"),
      })).describe("Array of columns to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_column", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create column failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
