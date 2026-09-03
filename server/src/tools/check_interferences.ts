import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCheckInterferencesTool(server: McpServer) {
  server.tool(
    "check_interferences",
    "Check interference/collision between Revit elements. Returns pairs of colliding element ids.",
    {
      elementIds: z.array(z.number().int().positive()).min(2),
    },
    async (args) => callRevit("check_interferences", args)
  );
}
