import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId, RGB } from "../utils/schemas.js";

export function registerSetCategoryOverridesTool(server: McpServer) {
  server.tool(
    "set_category_overrides",
    "Set graphic overrides for a category in a Revit view. Requires a valid viewId and categoryId. Returns the result of applying the overrides.",
    {
      viewId: ElementId,
      categoryId: ElementId,
      overrides: z.object({
        color: RGB.optional().describe("Sets projection and cut line color only"),
        lineWeight: z.number().int().optional(),
        fillPattern: z.string().optional().describe("Surface foreground fill pattern name"),
        halftone: z.boolean().optional(),
        transparency: z.number().int().min(0).max(100).optional().describe("0–100"),
      }),
    },
    async (args) => callRevit("set_category_overrides", args)
  );
}
