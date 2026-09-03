import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerCreateRevisionCloudTool(server: McpServer) {
  server.tool(
    "create_revision_cloud",
    "Creates a revision cloud on a view (mm). Needs an existing revision and view. Returns the new element id.",
    {
      revisionId: ElementId.describe("Revision to associate with"),
      viewId: ElementId.describe("View to place the cloud in"),
      points: z.array(z.object({
        x: z.number(),
        y: z.number(),
      })).describe("Boundary points (mm)"),
    },
    async (args) => callRevit("create_revision_cloud", args)
  );
}
