import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateDimensionsTool(server: McpServer) {
  server.tool(
    "create_dimensions",
    "Create dimension annotations in the active Revit view (mm). Each entry needs start/end points; supply elementIds to extract references or linePoint to position the line. Returns created element ids.",
    {
      dimensions: z.array(
        z.object({
          startPoint: Pt,
          endPoint: Pt,
          linePoint: Pt.optional().describe("Dimension line location; defaults to midpoint offset"),
          elementIds: z
            .array(z.number())
            .optional()
            .describe("IDs to dimension; auto-detected if omitted"),
          dimensionStyleId: z.number().optional().default(-1).describe("-1 = default style"),
          viewId: z.number().optional().default(-1).describe("-1 = active view"),
          options: z.record(z.union([z.string(), z.number()])).optional().describe("Named Revit params to set on the dimension"),
        })
      ),
    },
    async (args) => callRevit("create_dimensions", { dimensions: args.dimensions })
  );
}
