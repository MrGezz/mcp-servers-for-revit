import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateDraftingViewTool(server: McpServer) {
  server.tool(
    "create_drafting_view",
    "Creates a drafting view. Returns the new view's id. The view name appears in the message string.",
    {
      name: z.string().optional(),
      scale: z.number().int().optional().default(100).describe("e.g. 100 for 1:100"),
      detailLevel: z.enum(["Coarse", "Medium", "Fine"]).optional().default("Coarse"),
    },
    async (args) => callRevit("create_drafting_view", args)
  );
}
