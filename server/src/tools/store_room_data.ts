import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { memoryOp, projectId, roomId, toProps } from "../memory/legacyBridge.js";

const RoomSchema = z.object({
  room_id: z.string().describe("Unique identifier for the room (Revit Element ID)"),
  room_name: z.string().optional().describe("Room name"),
  room_number: z.string().optional().describe("Room number"),
  department: z.string().optional().describe("Department"),
  level: z.string().optional().describe("Level or floor"),
  area: z.number().optional().describe("Room area"),
  perimeter: z.number().optional().describe("Room perimeter"),
  occupancy: z.string().optional().describe("Occupancy type"),
  comments: z.string().optional().describe("Additional comments"),
  metadata: z.record(z.string()).optional().describe("Additional room metadata as key-value pairs"),
});

export function registerStoreRoomDataTool(server: McpServer) {
  server.tool(
    "store_room_data",
    "Store room data in the current Revit model, linked to its project. The data is written INTO the " +
      "model via Extensible Storage, so it travels with the file. Each room becomes an entity and is " +
      "linked to the project by a 'contains' relation, which is what makes " +
      "query_stored_data able to answer 'which rooms belong to this project'.",
    {
      project_name: z.string().describe("The name of the Revit project this room belongs to"),
      rooms: z.array(RoomSchema).describe("Array of room data to store"),
    },
    async (args: any) => {
      try {
        const pid = projectId(args.project_name);

        // The project entity is written alongside the rooms. Without it the
        // 'contains' relations would be dangling, and the graph would reject them -
        // which is the correct behaviour, so the fix is to supply the endpoint
        // rather than to relax the check.
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
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text" as const,
              text:
                "store_room_data failed: " +
                (error instanceof Error ? error.message : String(error)) +
                "\n\nThis tool now writes into the open Revit model, so it needs a live connection " +
                "and an open document.",
            },
          ],
          isError: true as const,
        };
      }
    }
  );
}
