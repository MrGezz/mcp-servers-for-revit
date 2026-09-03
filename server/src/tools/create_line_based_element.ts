import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Line } from "../utils/schemas.js";

export function registerCreateLineBasedElementTool(server: McpServer) {
  server.tool(
    "create_line_based_element",
    "Batch-create line-based elements in mm: walls (OST_Walls), ducts (OST_DuctCurves) or a family-based category such as beams (OST_StructuralFraming) via typeId. Not for pipes or conduits (use create_pipe / create_conduit). Returns the new element ids.",
    {
      data: z
        .array(
          z.object({
            category: z
              .string()
              .describe("Revit built-in category (e.g., OST_Walls, OST_StructuralFraming)"),
            typeId: z.number().optional(),
            locationLine: Line,
            thickness: z.number(),
            height: z.number(),
            baseLevel: z.number(),
            baseOffset: z.number(),
          })
        )
        .describe("Array of line-based elements to create"),
    },
    async (args) => callRevit("create_line_based_element", args)
  );
}
