import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId, RGB } from "../utils/schemas.js";

export function registerManageViewFiltersTool(server: McpServer) {
  server.tool(
    "manage_view_filters",
    "Add or remove a named view filter on a view; optionally set graphic overrides (color, line weight, fill pattern, halftone, visibility). Needs a valid viewId. Returns the result.",
    {
      viewId: ElementId,
      action: z.enum(["add", "remove"]),
      filterName: z.string(),
      overrides: z.object({
        visible: z.boolean().optional(),
        color: RGB.optional().describe("Projection line color (r,g,b 0-255)"),
        lineWeight: z.number().int().optional(),
        fillPattern: z.string().optional().describe("Fill pattern name"),
        halftone: z.boolean().optional(),
      }).optional().describe("Graphic overrides (for add action)"),
    },
    async (args) => callRevit("manage_view_filters", args)
  );
}
