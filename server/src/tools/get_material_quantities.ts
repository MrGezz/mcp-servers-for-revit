import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerGetMaterialQuantitiesTool(server: McpServer) {
  server.tool(
    "get_material_quantities",
    "Material takeoff: per material name, class, areaM2, volumeM3 and element count, plus totals. Filter by BuiltInCategory names (OST_Walls...; an unknown name is an error, not ignored). includeElementIds adds id lists, which can be large.",
    {
      categoryFilters: z.array(z.string()).optional().describe("BuiltInCategory names; all if omitted"),
      selectedElementsOnly: z.boolean().optional().default(false).describe("Only the current selection"),
      includeElementIds: z.boolean().optional().default(false).describe("Add per-material element id lists"),
    },
    async (args) =>
      callRevit("get_material_quantities", {
        categoryFilters: args.categoryFilters ?? null,
        selectedElementsOnly: args.selectedElementsOnly ?? false,
        includeElementIds: args.includeElementIds ?? false,
      })
  );
}
