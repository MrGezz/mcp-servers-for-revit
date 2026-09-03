import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerPlaceViewOnSheetTool(server: McpServer) {
  server.tool(
    "place_view_on_sheet",
    "Place one or more views onto sheets as viewports (positions in mm). Requires valid sheetId and viewId. Returns created viewport ids.",
    {
      data: z
        .array(
          z.object({
            sheetId: z.number(),
            viewId: z.number(),
            positionX: z.number(),
            positionY: z.number(),
            viewportTypeId: z.number().optional(),
            displayTitle: z
              .boolean()
              .optional()
              .describe("Sets detail-number field, not title visibility"),
            scaleOverride: z.number().optional().describe("Override scale denominator"),
            rotation: z
              .number()
              .optional()
              .describe("0=None, 1=Clockwise, 2=CounterClockwise"),
            parameters: z.record(z.any()).optional(),
          })
        )
        .describe("Array of viewports to place"),
    },
    async (args) => callRevit("place_view_on_sheet", args)
  );
}
