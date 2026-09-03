import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerDuplicateTypeTool(server: McpServer) {
  server.tool(
    "duplicate_type",
    "Duplicates an element type and assigns a new name. Returns the new type element ID.",
    {
      typeId: ElementId.describe("Element type ID to duplicate"),
      newName: z.string().min(1),
    },
    async (args) => callRevit("duplicate_type", { typeId: args.typeId, newName: args.newName })
  );
}
