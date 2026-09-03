import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerPlaceScheduleOnSheetTool(server: McpServer) {
  server.tool(
    "place_schedule_on_sheet",
    "Places a schedule view onto a sheet at the given (x, y) position (mm). Requires valid schedule and sheet element IDs. Returns the ScheduleSheetInstance element ID.",
    {
      scheduleId: ElementId,
      sheetId: ElementId,
      location: z.object({
        x: z.number(),
        y: z.number(),
      }),
    },
    async (args) => callRevit("place_schedule_on_sheet", args)
  );
}
