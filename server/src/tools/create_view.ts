import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateViewTool(server: McpServer) {
  server.tool(
    "create_view",
    "Create one or more views (floor plans, ceiling plans, sections, elevations, 3D) in the active project. levelElevation in mm. Returns created view ids.",
    {
      data: z
        .array(
          z.object({
            name: z.string().optional(),
            viewType: z
              .string()
              .optional()
              .describe("FloorPlan, CeilingPlan, Elevation, Section, 3D"),
            levelElevation: z
              .number()
              .optional()
              .describe("Level elevation in mm"),
            detailLevel: z
              .string()
              .optional()
              .describe("Coarse, Medium, or Fine"),
            scale: z.number().optional(),
            viewFamilyTypeName: z.string().optional(),
            templateId: z.string().optional().describe("Template view ID"),
            direction: z
              .object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional(),
              })
              .optional()
              .describe("Section view facing direction (not used for elevations)"),
            parameters: z.record(z.any()).optional(),
          })
        )
        .describe("Array of views to create"),
    },
    async (args) => callRevit("create_view", args)
  );
}
