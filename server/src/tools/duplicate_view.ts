import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerDuplicateViewTool(server: McpServer) {
  server.tool(
    "duplicate_view",
    "Duplicates a Revit view. Mode controls whether detailing and dependents are copied. Returns the new view's ID.",
    {
      viewId: ElementId.describe("Source view ID to duplicate"),
      mode: z.enum(["duplicate", "with_detailing", "dependent"]).optional().default("duplicate"),
      newName: z.string().optional(),
    },
    async (args) => callRevit("duplicate_view", args)
  );
}
