import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerTransformElementsTool(server: McpServer) {
  server.tool(
    "transform_elements",
    "Move, copy, rotate, or mirror Revit elements. Returns new element IDs for copy operations.",
    {
      elementIds: z.array(z.number().int().positive()).min(1).describe("Array of element IDs to transform"),
      transformType: z.enum(["move", "copy", "rotate", "mirror"]).describe("Type of transform: move, copy, rotate, or mirror"),
      params: z.object({
        dx: z.number().optional().describe("Translation X in feet (for move/copy)"),
        dy: z.number().optional().describe("Translation Y in feet (for move/copy)"),
        dz: z.number().optional().describe("Translation Z in feet (for move/copy)"),
        angle: z.number().optional().describe("Rotation angle in radians (for rotate)"),
        axis: z.object({ x: z.number().optional(), y: z.number().optional(), z: z.number().optional() }).optional().describe("Rotation axis vector (for rotate)"),
        origin: z.object({ x: z.number().optional(), y: z.number().optional(), z: z.number().optional() }).optional().describe("Origin point for rotate/mirror"),
        normal: z.object({ x: z.number().optional(), y: z.number().optional(), z: z.number().optional() }).optional().describe("Mirror plane normal (for mirror)"),
      }).describe("Transform parameters"),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds,
        transformType: args.transformType,
        params: args.params,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("transform_elements", params);
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
              text: `Transform elements failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
