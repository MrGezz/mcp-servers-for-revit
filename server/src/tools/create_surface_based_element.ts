import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Line } from "../utils/schemas.js";

export function registerCreateSurfaceBasedElementTool(server: McpServer) {
  server.tool(
    "create_surface_based_element",
    "Create floors, ceilings or roofs (mm) from a closed boundary of line segments. Give typeId (from get_available_family_types) or category; element thickness comes from the selected type. Returns the new element ids.",
    {
      data: z
        .array(
          z.object({
            category: z
              .enum(["OST_Floors", "OST_Ceilings", "OST_Roofs"])
              .optional()
              .describe("Revit category; inferred from typeId if omitted"),
            typeId: z
              .number()
              .optional()
              .describe("Family type ElementId"),
            boundary: z
              .object({
                outerLoop: z
                  .array(Line)
                  .min(3)
                  .describe("Closed boundary as line segments"),
              }),
            baseLevel: z.number().describe("Absolute elevation in mm"),
            baseOffset: z.number().describe("Additional offset from level in mm"),
          })
        ),
    },
    async (args) => callRevit("create_surface_based_element", args)
  );
}
