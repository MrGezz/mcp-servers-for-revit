import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateLevelTool(server: McpServer) {
  server.tool(
    "create_level",
    "Creates one or more levels in the Revit document at specified elevations (mm). Levels host floor plans, ceilings, and story-based elements. Returns created level ids and names.",
    {
      data: z.array(
        z.object({
          name: z.string(),
          elevation: z.number().describe("Elevation in mm from project origin"),
          isBuildingStory: z.boolean().default(true),
          createFloorPlan: z.boolean().default(true),
          createCeilingPlan: z.boolean().default(true),
        })
      ),
    },
    async (args) => callRevit("create_level", args)
  );
}
