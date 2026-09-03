import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerPlaceFamilyInstanceTool(server: McpServer) {
  server.tool(
    "place_family_instance",
    "Place family instances (mm). Requires symbolId and placementType; hostId is required for Hosted and FaceBased. Returns placed element ids.",
    {
      data: z.array(z.object({
        symbolId: z.number().describe("FamilySymbol ElementId"),
        placementType: z.enum(["Unhosted", "Hosted", "FaceBased", "Workplane"]),
        location: Pt,
        hostId: z.number().optional().describe("Required for Hosted and FaceBased placement"),
        level: z.number().optional().describe("Level elevation"),
        rotation: z.number().optional().describe("Degrees around Z-axis"),
      })),
    },
    async (args) => callRevit("place_family_instance", args)
  );
}
