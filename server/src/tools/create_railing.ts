import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateRailingTool(server: McpServer) {
  server.tool(
    "create_railing",
    "Creates railings in a Revit model from an array of start/end point pairs. Units: mm. Level snaps to nearest existing level by elevation. Returns created element IDs.",
    {
      data: z.array(z.object({
        startPoint: Pt,
        endPoint: Pt,
        height: z.number().optional(),
        level: z.number().describe("Base level elevation in mm"),
        typeId: z.number().optional(),
        railingType: z.string().optional(),
      })),
    },
    async (args) => callRevit("create_railing", args)
  );
}
