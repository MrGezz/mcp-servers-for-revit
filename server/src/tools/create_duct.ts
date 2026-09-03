import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateDuctTool(server: McpServer) {
  server.tool(
    "create_duct",
    "Create ducts (mm). Auto-fallback selects a rectangular duct type when no typeId given; pass a round-duct typeId for round ducts. Width/height set duct dimensions. Returns new element ids.",
    {
      data: z.array(z.object({
        startPoint: Pt,
        endPoint: Pt,
        width: z.number().describe("Width or diameter in mm"),
        height: z.number().optional().describe("Rectangular ducts only, mm"),
        baseLevel: z.number(),
        baseOffset: z.number().optional(),
        systemType: z.string().optional().describe("Supply Air, Return Air, Exhaust Air"),
        typeId: z.number().optional(),
      })),
    },
    async (args) => callRevit("create_duct", args)
  );
}
