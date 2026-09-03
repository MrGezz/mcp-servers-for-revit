import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateFloorTool(server: McpServer) {
  server.tool(
    "create_floor",
    "Create floors (mm). Provide boundary points, base level elevation, and optional type. Returns created element ids.",
    {
      data: z.array(z.object({
        level: z.number().describe("Base level elevation in mm"),
        boundaryPoints: z.array(Pt).describe("Floor boundary vertices (mm)"),
        levelOffset: z.number().optional().describe("Height offset from level (mm)"),
        typeId: z.number().optional().describe("Floor type ElementId"),
        floorType: z.string().optional().describe("Floor type name"),
        isStructural: z.boolean().optional().describe("Mark floor as structural"),
      })),
    },
    async (args) => callRevit("create_floor", args)
  );
}
