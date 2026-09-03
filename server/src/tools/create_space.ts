import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateSpaceTool(server: McpServer) {
  server.tool(
    "create_space",
    "Create spaces in the Revit model for MEP analysis (mm). Returns created space ids.",
    {
      data: z.array(z.object({
        name: z.string(),
        number: z.string(),
        location: Pt,
        baseLevel: z.number().describe("Base level elevation (mm)"),
        department: z.string().optional().describe('Department string for the space'),
      })),
    },
    async (args) => callRevit("create_space", args)
  );
}
