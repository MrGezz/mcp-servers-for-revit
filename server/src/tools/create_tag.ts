import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateTagTool(server: McpServer) {
  server.tool(
    "create_tag",
    "Create tag annotations on elements in the current Revit view. Supports tagging doors, windows, walls, rooms, and other elements with configurable tag type, orientation, leader, and location. All coordinates are in millimeters (mm).",
    {
      data: z
        .array(
          z.object({
            elementId: z
              .number()
              .describe("Element ID of the element to tag"),
            location: z
              .object({
                x: z.number().describe("X coordinate in mm"),
                y: z.number().describe("Y coordinate in mm"),
                z: z.number().describe("Z coordinate in mm"),
              })
              .describe("Tag placement location in mm"),
            orientation: z
              .number()
              .optional()
              .default(0)
              .describe("Tag orientation (0=Horizontal, 1=Vertical)"),
            hasLeader: z
              .boolean()
              .optional()
              .default(false)
              .describe("Whether the tag has a leader line"),
            tagTypeId: z
              .number()
              .optional()
              .default(-1)
              .describe("Element ID of the tag type. -1 for default"),
            tagCategory: z
              .string()
              .optional()
              .default("")
              .describe("Tag category (Door, Window, Wall, Room, Multi)"),
            viewId: z
              .number()
              .optional()
              .default(-1)
              .describe("Element ID of the view. -1 for active view"),
          })
        )
        .describe("Array of tags to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_tag", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Tag creation failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
