import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreatePipeTool(server: McpServer) {
  server.tool(
    "create_pipe",
    "Create pipes in the Revit model (mm). Each entry needs start/end points, diameter, and base level. Returns new element ids.",
    {
      data: z.array(z.object({
        startPoint: Pt,
        endPoint: Pt,
        diameter: z.number(),
        baseLevel: z.number().describe("Base level elevation"),
        baseOffset: z.number().optional(),
        systemType: z.string().optional().describe("e.g. Domestic Cold Water, Sanitary"),
        pipeType: z.string().optional().describe("Pipe type name"),
        typeId: z.number().optional().describe("Pipe type ElementId"),
      })),
    },
    async (args) => callRevit("create_pipe", args)
  );
}
