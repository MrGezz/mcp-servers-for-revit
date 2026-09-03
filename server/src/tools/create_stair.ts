import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateStairTool(server: McpServer) {
  server.tool(
    "create_stair",
    "Creates straight-run stairs in Revit (mm). Requires base/top level elevations and either startPoint+endPoint or pathPoints (≥2) for run geometry. Optional landing and type. Returns stair element ids. Requires Revit 2022+.",
    {
      data: z.array(z.object({
        startPoint: Pt.optional().describe("Start of stair run"),
        endPoint: Pt.optional().describe("End of stair run"),
        pathPoints: z.array(Pt).optional().describe("Path points for multi-run stairs (min 2)"),
        baseLevel: z.number().describe("Base level elevation (mm)"),
        topLevel: z.number().describe("Top level elevation (mm)"),
        width: z.number().optional().describe("Landing fallback size (mm); run width set by type"),
        typeId: z.number().optional(),
        stairType: z.string().optional(),
        hasLanding: z.boolean().optional(),
        landingWidth: z.number().optional(),
        landingDepth: z.number().optional(),
      })),
    },
    async (args) => callRevit("create_stair", args)
  );
}
