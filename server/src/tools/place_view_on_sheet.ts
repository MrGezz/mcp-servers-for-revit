import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerPlaceViewOnSheetTool(server: McpServer) {
  server.tool(
    "place_view_on_sheet",
    "Place one or more views onto sheets in Revit as viewports. Supports position control in mm, viewport type, title display, scale override, and rotation.",
    {
      data: z
        .array(
          z.object({
            sheetId: z
              .number()
              .describe("Sheet ID to place viewport on"),
            viewId: z
              .number()
              .describe("View ID to place as a viewport"),
            positionX: z
              .number()
              .describe("X position on sheet in mm"),
            positionY: z
              .number()
              .describe("Y position on sheet in mm"),
            viewportTypeId: z
              .number()
              .optional()
              .describe("Viewport type ID"),
            displayTitle: z
              .boolean()
              .optional()
              .describe("Whether to display the view title"),
            scaleOverride: z
              .number()
              .optional()
              .describe("Override scale for the viewport"),
            labelText: z
              .string()
              .optional()
              .describe("Viewport label text"),
            rotation: z
              .number()
              .optional()
              .describe("Rotation angle in degrees"),
            parameters: z
              .record(z.any())
              .optional()
              .describe("Additional viewport parameters"),
          })
        )
        .describe("Array of viewports to place"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("place_view_on_sheet", params);
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
              text: `Place view on sheet failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
