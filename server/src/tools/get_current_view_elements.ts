import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { errorMessage, fail, fromRevit } from "../utils/reply.js";
import { Limit } from "../utils/schemas.js";

type ElementRow = { Id?: number; Properties?: Record<string, string> };

export function registerGetCurrentViewElementsTool(server: McpServer) {
  server.tool(
    "get_current_view_elements",
    "List elements visible in the active view: id, name, category, family/type names, location (LocationMm or StartMm/EndMm/LengthMm, millimetres) and Comments/Mark/Level. Filter by BuiltInCategory names (OST_Walls, OST_Doors, OST_Dimensions...). Returns the first `limit` records (default 30) plus the total count.",
    {
      modelCategoryList: z.array(z.string()).optional().describe("Model categories, e.g. OST_Walls"),
      annotationCategoryList: z.array(z.string()).optional().describe("Annotation categories, e.g. OST_TextNotes"),
      includeHidden: z.boolean().optional().describe("Include elements hidden in the view"),
      limit: Limit(30),
    },
    async (args) => {
      try {
        const response = (await withRevitConnection(async (client) =>
          client.sendCommand("get_current_view_elements", {
            modelCategoryList: args.modelCategoryList ?? [],
            annotationCategoryList: args.annotationCategoryList ?? [],
            includeHidden: args.includeHidden ?? false,
            limit: args.limit ?? 30,
          })
        )) as { Elements?: ElementRow[] } | null;

        // The handler repeats the id inside Properties as a string; one copy is enough.
        for (const row of response?.Elements ?? []) {
          if (row.Properties && row.Properties.ElementId === String(row.Id)) delete row.Properties.ElementId;
        }
        return fromRevit(response, "get_current_view_elements");
      } catch (error) {
        return fail(`get_current_view_elements failed: ${errorMessage(error)}`);
      }
    }
  );
}
