import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateScheduleTool(server: McpServer) {
  server.tool(
    "create_schedule",
    "Create one or more Revit schedules (regular, material, keynote, viewList, sheetList, revision). Specify category by id or name. Returns created schedule ids.",
    {
      data: z
        .array(
          z.object({
            name: z.string().optional(),
            type: z
              .string()
              .optional()
              .describe("regular, material, keynote, viewList, sheetList, revision"),
            categoryId: z.number().optional(),
            categoryName: z.string().optional(),
            templateId: z.string().optional().describe("View template id to apply"),
            showTitle: z.boolean().optional(),
            showHeaders: z.boolean().optional(),
            showGridLines: z.boolean().optional(),
            showOutlines: z.boolean().optional().describe("Not settable via Revit API (2022-2027); produces a warning only"),
            parameters: z.record(z.any()).optional(),
          })
        )
        .describe("Array of schedule definitions to create"),
    },
    async (args) => callRevit("create_schedule", args)
  );
}
