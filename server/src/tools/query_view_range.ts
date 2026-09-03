import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerQueryViewRangeTool(server: McpServer) {
  server.tool(
    "query_view_range",
    "Get the view range of a plan view. Returns top, cut plane, bottom, and view depth planes with their level ids and OffsetMm.",
    {
      viewId: ElementId.describe("Plan view element id"),
    },
    async (args) => callRevit("query_view_range", { viewId: args.viewId })
  );
}
