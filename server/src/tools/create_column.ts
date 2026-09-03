import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateColumnTool(server: McpServer) {
  server.tool(
    "create_column",
    "Creates structural columns in Revit (mm). Needs base level elevation per column. Optionally specify type by typeId or type name. Returns created element ids.",
    {
      data: z.array(z.object({
        location: Pt,
        height: z.number(),
        baseLevel: z.number().describe("Base level elevation in mm"),
        typeId: z.number().optional().describe("Column type ElementId"),
        type: z.string().optional().describe("Column type name"),
        isStructural: z.boolean().optional(),
      })),
    },
    async (args) => callRevit("create_column", args)
  );
}
