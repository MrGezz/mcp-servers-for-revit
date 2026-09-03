import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerTagAllRoomsTool(server: McpServer) {
  server.tool(
    "tag_all_rooms",
    "Tags all rooms in the active or matching floor plan view at each room's center. Auto-switches to a floor plan view if needed; fails only when none exists for the rooms' level. Returns created tag ids.",
    {
      useLeader: z
        .boolean()
        .optional()
        .default(false)
        .describe("Add a leader line to each tag"),
      tagTypeId: z
        .string()
        .optional()
        .describe("Room tag family type ID; best-effort, falls back to first available if invalid"),
      roomIds: z
        .array(z.number())
        .optional()
        .describe("Room element IDs to tag; all rooms if omitted"),
    },
    async (args) => callRevit("tag_rooms", args)
  );
}
