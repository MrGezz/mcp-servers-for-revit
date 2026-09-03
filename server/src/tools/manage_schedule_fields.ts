import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerManageScheduleFieldsTool(server: McpServer) {
  server.tool(
    "manage_schedule_fields",
    "Add, remove, reorder, hide, or show a field in a schedule view. Requires the schedule ElementId. Returns a success/failure result.",
    {
      scheduleId: z.number().int().describe("Schedule view ElementId"),
      action: z.enum(["add", "remove", "reorder", "hide", "show"]),
      fieldName: z.string(),
      position: z.number().int().min(0).optional().describe("0-based index; required for reorder, optional for add"),
    },
    async (args) => callRevit("manage_schedule_fields", args)
  );
}
