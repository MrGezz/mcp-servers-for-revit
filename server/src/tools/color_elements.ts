import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { errorMessage, fail, ok } from "../utils/reply.js";
import { RGB } from "../utils/schemas.js";

export function registerColorElementsTool(server: McpServer) {
  server.tool(
    "color_elements",
    "Colors elements in the current view by category and parameter value; each unique value gets a distinct color. Returns total element count and groups with assigned RGB colors.",
    {
      categoryName: z.string().describe("Human-readable Revit category, e.g. Walls, Doors"),
      parameterName: z.string().describe("Parameter to group and color elements by"),
      useGradient: z
        .boolean()
        .optional()
        .default(false)
        .describe("Use gradient instead of random colors"),
      customColors: z.array(RGB).optional(),
    },
    async (args) => {
      try {
        const response = (await withRevitConnection(async (client) =>
          client.sendCommand("color_splash", args)
        )) as any;

        if (response.success) {
          const coloredGroups = response.results || [];
          let resultText = `Successfully colored ${response.totalElements} elements across ${response.coloredGroups} groups.\n\nParameter Value Groups:\n`;
          coloredGroups.forEach((group: any) => {
            const rgb = group.color;
            resultText += `- "${group.parameterValue}": ${group.count} elements colored with RGB(${rgb.r}, ${rgb.g}, ${rgb.b})\n`;
          });
          return ok(resultText);
        } else {
          return fail(`Color operation failed: ${response.message}`);
        }
      } catch (error) {
        return fail(`Color operation failed: ${errorMessage(error)}`);
      }
    }
  );
}
