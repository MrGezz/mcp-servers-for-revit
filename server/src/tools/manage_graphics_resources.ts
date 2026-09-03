import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { RGB } from "../utils/schemas.js";

export function registerManageGraphicsResourcesTool(server: McpServer) {
  server.tool(
    "manage_graphics_resources",
    "Update existing Revit line styles. Requires an active document. Returns the operation result.",
    {
      action: z.enum(["line_style"]),
      name: z.string(),
      properties: z.object({
        color: RGB.optional(),
        lineWeight: z.number().int().optional().describe("Applicable to line_style"),
        linePattern: z.string().optional().describe("Pattern name; applicable to line_style"),
      }).optional(),
    },
    async (args) => callRevit("manage_graphics_resources", args)
  );
}
