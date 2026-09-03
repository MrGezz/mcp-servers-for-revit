import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerTagAllWallsTool(server: McpServer) {
  server.tool(
    "tag_all_walls",
    "Tags all walls in the current active view at each wall's midpoint. Needs an active plan/section view with walls. Returns tag objects with id, wallId, wallName, and location (x_mm/y_mm/z_mm).",
    {
      useLeader: z
        .boolean()
        .optional()
        .default(false)
        .describe("Add a leader line to each tag"),
      tagTypeId: z
        .string()
        .optional()
        .describe("Wall tag family type ID; default type used if omitted"),
    },
    async (args) => callRevit("tag_walls", args)
  );
}
