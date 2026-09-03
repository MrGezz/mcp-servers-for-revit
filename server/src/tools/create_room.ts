import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateRoomTool(server: McpServer) {
  server.tool(
    "create_room",
    "Create and place rooms at given mm locations; location must fall inside enclosed wall boundaries. Optionally assign name, number, level, offsets, and department. Returns new element ids.",
    {
      data: z
        .array(
          z.object({
            name: z.string(),
            number: z.string().optional(),
            location: Pt,
            levelId: z.number().optional().describe("Level ElementId; defaults to nearest level to z"),
            upperLimitId: z.number().optional().describe("Upper limit level ElementId"),
            limitOffset: z.number().optional().describe("Offset above upper limit, mm"),
            baseOffset: z.number().optional().describe("Offset above base level, mm"),
            department: z.string().optional(),
            comments: z.string().optional(),
          })
        )
        .describe("Array of rooms to create"),
    },
    async (args) => callRevit("create_room", args)
  );
}
