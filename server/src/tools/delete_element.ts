import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerDeleteElementTool(server: McpServer) {
  server.tool(
    "delete_element",
    "Delete elements by ElementId. Dependent elements (hosted doors, tags...) go with them. Reports which ids were deleted and which were not found. Confirm with the user first.",
    {
      elementIds: z.array(z.union([z.number().int(), z.string()])).min(1).describe("ElementIds to delete"),
    },
    async (args) => callRevit("delete_element", { elementIds: args.elementIds.map((id) => String(id)) })
  );
}
