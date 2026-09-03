import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerGetCurrentViewInfoTool(server: McpServer) {
  server.tool(
    "get_current_view_info",
    "Returns type, name, scale, and other properties of the active Revit view.",
    {},
    async (args) => callRevit("get_current_view_info", {})
  );
}
