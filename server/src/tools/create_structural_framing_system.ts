import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateStructuralFramingSystemTool(server: McpServer) {
  server.tool(
    "create_structural_framing_system",
    "Creates a beam framing system in a rectangular boundary at fixed spacing (mm). Level auto-created if it follows 'Level N' pattern at 4000mm height. Returns the created beam system element.",
    {
      levelName: z
        .string()
        .describe("Level name; 'Level N' pattern auto-creates it"),
      xMin: z.number(),
      xMax: z.number(),
      yMin: z.number(),
      yMax: z.number(),
      spacing: z.number().positive(),
      directionEdge: z
        .enum(["bottom", "right", "top", "left"])
        .default("bottom")
        .describe("Edge beams run perpendicular to"),
      layoutRule: z
        .enum(["fixed_distance"])
        .default("fixed_distance"),
      justify: z
        .enum(["beginning", "center", "end", "directionline"])
        .default("center"),
      beamTypeName: z
        .string()
        .optional()
        .describe("Family type name; first available if omitted"),
      elevation: z.number().default(0),
      is3d: z.boolean().default(false),
    },
    async (args) => callRevit("create_structural_framing_system", args)
  );
}
