import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateSheetTool(server: McpServer) {
  server.tool(
    "create_sheet",
    "Create one or more sheets in Revit with optional title blocks. Supports sheet numbering, naming, title block selection by ID or family name, and revision assignment.",
    {
      data: z
        .array(
          z.object({
            sheetNumber: z
              .string()
              .optional()
              .describe("Sheet number (e.g., A101)"),
            sheetName: z
              .string()
              .optional()
              .describe("Sheet name"),
            titleBlockTypeId: z
              .number()
              .optional()
              .describe("Title block type ID"),
            titleBlockFamilyName: z
              .string()
              .optional()
              .describe("Title block family name"),
            titleBlockTypeName: z
              .string()
              .optional()
              .describe("Title block type name"),
            revisionIds: z
              .array(z.number())
              .optional()
              .describe("Revision IDs to apply"),
            parameters: z
              .record(z.any())
              .optional()
              .describe("Additional sheet parameters"),
          })
        )
        .describe("Array of sheets to create"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_sheet", params);
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
              text: `Create sheet failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
