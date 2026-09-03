import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { memoryOp, projectId, roomId, toProps } from "../memory/legacyBridge.js";
import { fromRevit, fail, errorMessage } from "../utils/reply.js";

const RoomSchema = z.object({
  room_id: z.string().describe("Revit Element ID for this room"),
  room_name: z.string().optional(),
  room_number: z.string().optional(),
  department: z.string().optional(),
  level: z.string().optional(),
  area: z.number().optional(),
  perimeter: z.number().optional(),
  occupancy: z.string().optional(),
  comments: z.string().optional(),
  metadata: z.record(z.string()).optional().describe("Extra key-value pairs"),
});

export function registerStoreRoomDataTool(server: McpServer) {
  server.tool(
    "store_room_data",
    "Stores room entities and a project entity in Revit Extensible Storage (travels with the file). Rooms are linked to the project via 'contains' relations, making them queryable by project.",
    {
      project_name: z.string().describe("Revit project this room belongs to"),
      rooms: z.array(RoomSchema),
    },
    async (args) => {
      try {
        const pid = projectId(args.project_name);

        const entities: any[] = [
          { id: pid, kind: "project", name: args.project_name, props: {} },
        ];
        const relations: any[] = [];

        for (const r of args.rooms ?? []) {
          const { room_id, room_name, metadata, ...rest } = r;
          entities.push({
            id: roomId(room_id),
            kind: "room",
            name: room_name ?? room_id,
            elementId: Number(room_id) || 0,
            props: { ...toProps(rest), ...toProps(metadata) },
          });
          relations.push({ from: pid, to: roomId(room_id), kind: "contains" });
        }

        const response = await memoryOp("write", { entities, relations });
        return fromRevit(response);
      } catch (error) {
        return fail(`store_room_data failed: ${errorMessage(error)}`, {
          hint: "Needs a live Revit connection and an open document.",
        });
      }
    }
  );
}
