import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageScheduleFieldsTool(server: McpServer) {
  server.tool(
    "manage_schedule_fields",
    "Manage fields in a Revit schedule: add, remove, reorder, hide, or show fields.",
    {
      scheduleId: z.number().int().describe("Schedule view ID"),
      action: z.enum(["add", "remove", "reorder", "hide", "show"]).describe("Action to perform"),
      fieldName: z.string().describe("Field name"),
      position: z.number().int().min(0).optional().describe("Position index (for add or reorder)"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_schedule_fields", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Manage schedule fields failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
