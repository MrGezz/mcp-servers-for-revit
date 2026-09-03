import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerCreateCalloutTool(server: McpServer) {
  server.tool(
    "create_callout",
    "Create a callout view from a host view (mm). Requires a valid host view id and a Section ViewFamilyType in the project. Returns the element ID of the new callout view.",
    {
      name: z.string().optional().describe("Callout view name"),
      hostViewId: ElementId,
      boundingBox: z.object({
        minX: z.number(),
        minY: z.number(),
        maxX: z.number(),
        maxY: z.number(),
      }),
    },
    async (args) => callRevit("create_callout", args)
  );
}
