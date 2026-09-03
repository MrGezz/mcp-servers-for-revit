import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateSectionViewTool(server: McpServer) {
  server.tool(
    "create_section_view",
    "Create a section view (mm) with a bounding box and view family type. Returns the new view id.",
    {
      name: z.string().optional(),
      boundingBox: z.object({
        minX: z.number().optional().default(-50000),
        minY: z.number().optional().default(-50000),
        minZ: z.number().optional().default(-50000),
        maxX: z.number().optional().default(50000),
        maxY: z.number().optional().default(50000),
        maxZ: z.number().optional().default(50000),
      }).optional().default({}),
      viewFamilyTypeName: z.string().optional().default("Section"),
    },
    async (args) => callRevit("create_section_view", args)
  );
}
