import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateSheetTool(server: McpServer) {
  server.tool(
    "create_sheet",
    "Create one or more sheets in Revit. Supports sheet number, name, title block by id or family name, and revision assignment. Returns created sheet ids.",
    {
      data: z
        .array(
          z.object({
            sheetNumber: z.string().optional(),
            sheetName: z.string().optional(),
            titleBlockTypeId: z.number().optional(),
            titleBlockFamilyName: z.string().optional(),
            titleBlockTypeName: z.string().optional(),
            revisionIds: z.array(z.number()).optional(),
            parameters: z.record(z.any()).optional(),
          })
        )
        .describe("Array of sheets to create"),
    },
    async (args) => callRevit("create_sheet", args)
  );
}
