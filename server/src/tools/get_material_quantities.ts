import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerGetMaterialQuantitiesTool(server: McpServer) {
  server.tool(
    "get_material_quantities",
    "Calculate material quantities and takeoffs from the current Revit project: name, class, area, " +
      "volume and element count per material. Per-element ID lists are OMITTED by default - they were " +
      "90.1% of a 328,971-character response on an ordinary model and overflowed the client limit. Pass " +
      "includeElementIds to get them.",
    {
      categoryFilters: z
        .array(z.string())
        .optional()
        .describe("Optional list of Revit category names to filter by (e.g., ['OST_Walls', 'OST_Floors', 'OST_Roofs']). If not specified, all categories are included."),
      selectedElementsOnly: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to only analyze currently selected elements. Defaults to false (analyze entire project)."),
      includeElementIds: z
        .boolean()
        .optional()
        .default(false)
        .describe(
          "Include the per-material list of element IDs. Defaults to false: on a real model this list " +
            "was 16,831 ids and 90.1% of the whole response. The element COUNT is always reported."
        ),
    },
    async (args, extra) => {
      const params = {
        categoryFilters: args.categoryFilters ?? null,
        selectedElementsOnly: args.selectedElementsOnly ?? false,
        includeElementIds: args.includeElementIds ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_material_quantities", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Get material quantities failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
