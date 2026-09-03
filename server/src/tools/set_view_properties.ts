import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerSetViewPropertiesTool(server: McpServer) {
  server.tool(
    "set_view_properties",
    "Sets scale, detail level, display style, crop box (mm), or view template on a Revit view. Requires a valid viewId. Returns a success confirmation.",
    {
      viewId: ElementId,
      properties: z.object({
        scale: z.number().int().optional(),
        detailLevel: z.enum(["Coarse", "Medium", "Fine"]).optional(),
        displayStyle: z.enum(["wireframe", "hidden", "shaded", "consistent_colors", "realistic"]).optional(),
        cropBox: z.object({
          minX: z.number(),
          minY: z.number(),
          maxX: z.number(),
          maxY: z.number(),
        }).optional().describe("Crop box bounds in mm"),
        templateId: ElementId.optional(),
      }).describe("View properties to set"),
    },
    async (args) => callRevit("set_view_properties", args)
  );
}
