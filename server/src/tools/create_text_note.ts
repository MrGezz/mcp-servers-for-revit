import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateTextNoteTool(server: McpServer) {
  server.tool(
    "create_text_note",
    "Create text note annotations in the current Revit view. Supports multiple text notes with custom text content, location, rotation, width, alignment, and text note type. All coordinates are in millimeters (mm).",
    {
      data: z
        .array(
          z.object({
            location: z
              .object({
                x: z.number().describe("X coordinate in mm"),
                y: z.number().describe("Y coordinate in mm"),
                z: z.number().describe("Z coordinate in mm"),
              })
              .describe("Text note location point in mm"),
            text: z
              .string()
              .describe("Text content of the note"),
            rotation: z
              .number()
              .optional()
              .default(0)
              .describe("Text rotation in degrees"),
            width: z
              .number()
              .optional()
              .default(0)
              .describe("Text width in mm (0 = no width limit)"),
            textNoteTypeId: z
              .number()
              .optional()
              .default(-1)
              .describe("Element ID of the text note type. -1 for default"),
            viewId: z
              .number()
              .optional()
              .default(-1)
              .describe("Element ID of the view. -1 for active view"),
            horizontalAlign: z
              .number()
              .optional()
              .default(0)
              .describe("Horizontal alignment (0=Left, 1=Center, 2=Right)"),
            verticalAlign: z
              .number()
              .optional()
              .default(0)
              .describe("Vertical alignment (0=Top, 1=Middle, 2=Bottom)"),
          })
        )
        .describe("Array of text notes to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_text_note", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Text note creation failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
