import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerPlaceScheduleOnSheetTool(server: McpServer) {
  server.tool(
    "place_schedule_on_sheet",
    "Place a schedule view onto a sheet in Revit. All placement coordinates are in millimetres.",
    {
      scheduleId: z.number().int().describe("Element ID of the schedule view to place"),
      sheetId: z.number().int().describe("Element ID of the sheet to place the schedule on"),
      location: z.object({
        x: z.number().describe("X position on the sheet in millimetres"),
        y: z.number().describe("Y position on the sheet in millimetres"),
      }).describe("Placement location on the sheet in millimetres"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("place_schedule_on_sheet", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Place schedule on sheet failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
