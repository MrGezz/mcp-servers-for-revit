import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerCreateFilledRegionTool(server: McpServer) {
  server.tool(
    "create_filled_region",
    "Create a filled region in a view (mm). boundary is nested arrays of {x,y} points forming closed loops. Optionally specify the region type name. Returns the created element id.",
    {
      viewId: ElementId,
      boundary: z.array(z.array(z.object({
        x: z.number(),
        y: z.number(),
      }))).describe("Boundary loops (array of point arrays)"),
      filledRegionTypeName: z.string().optional(),
    },
    async (args) => callRevit("create_filled_region", args)
  );
}
