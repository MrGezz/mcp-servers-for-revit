import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerCreateDetailCurveTool(server: McpServer) {
  server.tool(
    "create_detail_curve",
    "Create detail lines in a Revit view (mm). Works in any 2D view that accepts detail elements (plan, ceiling plan, section, elevation, drafting). Returns ids of created detail curve elements.",
    {
      viewId: ElementId,
      lines: z.array(z.object({
        startX: z.number(),
        startY: z.number(),
        endX: z.number(),
        endY: z.number(),
      })),
    },
    async (args) => callRevit("create_detail_curve", args)
  );
}
