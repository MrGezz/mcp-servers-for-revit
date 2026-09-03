import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateElevationViewTool(server: McpServer) {
  server.tool(
    "create_elevation_view",
    "Create an elevation view via an elevation marker. Returns the new view id; the view name is embedded in the message string.",
    {
      name: z.string().optional(),
      directionIndex: z.number().int().min(0).max(3).optional().default(0).describe("Index 0-3; actual direction depends on marker orientation in project"),
      viewFamilyTypeName: z.string().optional().default("Elevation"),
    },
    async (args) => callRevit("create_elevation_view", args)
  );
}
