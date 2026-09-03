import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateTagTool(server: McpServer) {
  server.tool(
    "create_tag",
    "Adds annotation tags to elements in the active Revit view (mm). tagTypeId -1 uses default type, viewId -1 uses active view. tagCategory: Door, Window, Wall, Room, Multi. Returns created tag ids.",
    {
      data: z
        .array(
          z.object({
            elementId: z.number(),
            location: Pt,
            orientation: z
              .number()
              .optional()
              .default(0)
              .describe("0=Horizontal, 1=Vertical"),
            hasLeader: z.boolean().optional().default(false),
            tagTypeId: z
              .number()
              .optional()
              .default(-1)
              .describe("Tag type id; -1 for default"),
            tagCategory: z
              .string()
              .optional()
              .default("")
              .describe("Door, Window, Wall, Room, or Multi"),
            viewId: z
              .number()
              .optional()
              .default(-1)
              .describe("View id; -1 for active view"),
          })
        )
        .describe("Array of tags to create"),
    },
    async (args) => callRevit("create_tag", args)
  );
}
