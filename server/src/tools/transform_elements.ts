import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerTransformElementsTool(server: McpServer) {
  server.tool(
    "transform_elements",
    "Move, copy, rotate, or mirror elements. Coordinates and translation in feet, angle in radians. Copy returns the new element IDs; move/rotate/mirror return the IDs of the elements transformed.",
    {
      elementIds: z.array(z.number().int().positive()).min(1),
      transformType: z.enum(["move", "copy", "rotate", "mirror"]),
      params: z.object({
        dx: z.number().optional(),
        dy: z.number().optional(),
        dz: z.number().optional(),
        angle: z.number().optional().describe("Radians; used for rotate"),
        axis: z.object({ x: z.number().optional(), y: z.number().optional(), z: z.number().optional() }).optional().describe("Rotation axis vector"),
        origin: z.object({ x: z.number().optional(), y: z.number().optional(), z: z.number().optional() }).optional().describe("Origin for rotate/mirror, in feet"),
        normal: z.object({ x: z.number().optional(), y: z.number().optional(), z: z.number().optional() }).optional().describe("Mirror plane normal"),
      }),
    },
    async (args) => callRevit("transform_elements", {
      elementIds: args.elementIds,
      transformType: args.transformType,
      params: args.params,
    })
  );
}
