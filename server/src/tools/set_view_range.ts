import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerSetViewRangeTool(server: McpServer) {
  server.tool(
    "set_view_range",
    "Set the view range of a plan view (top, cut plane, bottom, view depth). Units: mm. Requires a plan view id.",
    {
      viewId: ElementId,
      topOffset: z.number().optional().default(0),
      cutOffset: z.number().optional().default(1200),
      bottomOffset: z.number().optional().default(0),
      viewDepthOffset: z.number().optional().default(0),
      topLevelId: ElementId.optional(),
    },
    async (args) => callRevit("set_view_range", args)
  );
}
