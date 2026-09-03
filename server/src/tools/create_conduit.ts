import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateConduitTool(server: McpServer) {
  server.tool(
    "create_conduit",
    "Create conduits in Revit (mm). Each conduit needs start/end points, diameter, and base level elevation. Returns new element ids.",
    {
      data: z.array(z.object({
        startPoint: Pt,
        endPoint: Pt,
        diameter: z.number(),
        baseLevel: z.number(),
        baseOffset: z.number().optional(),
        conduitType: z.string().optional().describe("Conduit type name"),
        typeId: z.number().optional().describe("Conduit type ElementId"),
      })),
    },
    async (args) => callRevit("create_conduit", args)
  );
}
