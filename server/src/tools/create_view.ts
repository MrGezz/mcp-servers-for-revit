import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateViewTool(server: McpServer) {
  server.tool(
    "create_view",
    "Create one or more views in Revit such as floor plans, ceiling plans, sections, elevations, or 3D views. Supports view type selection, level assignment, scale, detail level, and view template application. All units in millimeters (mm).",
    {
      data: z
        .array(
          z.object({
            name: z
              .string()
              .optional()
              .describe("View name"),
            viewType: z
              .string()
              .optional()
              .describe(
                "View type: FloorPlan, CeilingPlan, Elevation, Section, 3D"
              ),
            levelElevation: z
              .number()
              .optional()
              .describe(
                "Level elevation in mm (for plan/section/elevation views)"
              ),
            detailLevel: z
              .string()
              .optional()
              .describe("Detail level: Coarse, Medium, Fine"),
            scale: z
              .number()
              .optional()
              .describe("View scale (e.g., 100 for 1:100)"),
            viewFamilyTypeName: z
              .string()
              .optional()
              .describe("View family type name"),
            templateId: z
              .string()
              .optional()
              .describe("Template view ID to apply"),
            direction: z
              .object({
                x: z.number().optional().describe("X direction component"),
                y: z.number().optional().describe("Y direction component"),
                z: z.number().optional().describe("Z direction component"),
              })
              .optional()
              .describe(
                "View direction for elevation/section views"
              ),
            parameters: z
              .record(z.any())
              .optional()
              .describe(
                "Additional view parameters"
              ),
          })
        )
        .describe("Array of views to create"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_view", params);
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
              text: `Create view failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
