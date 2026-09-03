import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerCreateViewTemplateTool(server: McpServer) {
  server.tool(
    "create_view_template",
    "Create a view template from an existing view. Returns the new template id.",
    {
      sourceViewId: ElementId.describe("Source view ElementId"),
      name: z.string(),
    },
    async (args) => callRevit("create_view_template", args)
  );
}
