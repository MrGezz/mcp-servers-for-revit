import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateGroupTool(server: McpServer) {
  server.tool(
    "create_group",
    "Groups elements by id into named Revit model groups. Returns created group ids.",
    {
      data: z.array(z.object({
        elementIds: z.array(z.number()),
        name: z.string(),
      })),
    },
    async (args) => callRevit("create_group", args)
  );
}
