import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateRevisionTool(server: McpServer) {
  server.tool(
    "create_revision",
    "Creates a revision in the active Revit document. name sets the Revit Description field and takes priority over description when both are supplied. Returns the created revision id.",
    {
      name: z.string().describe("Sets revision Description; takes priority over description"),
      date: z.string().optional(),
      number: z.string().optional(),
      description: z.string().optional().describe("Sets Description only when name is omitted"),
    },
    async (args) => callRevit("create_revision", args)
  );
}
