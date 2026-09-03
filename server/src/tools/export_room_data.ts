import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerExportRoomDataTool(server: McpServer) {
  server.tool(
    "export_room_data",
    "Export every placed, enclosed room: id, name, number, level, areaM2, volumeM3, perimeterMm, unboundedHeightMm, department, phase, occupancy, status. Add unplaced rooms (no location) or not-enclosed rooms (placed, zero area) with the flags.",
    {
      includeUnplacedRooms: z.boolean().optional().default(false).describe("Also list rooms with no location"),
      includeNotEnclosedRooms: z.boolean().optional().default(false).describe("Also list placed rooms with zero area"),
    },
    async (args) =>
      callRevit("export_room_data", {
        includeUnplacedRooms: args.includeUnplacedRooms ?? false,
        includeNotEnclosedRooms: args.includeNotEnclosedRooms ?? false,
      })
  );
}
