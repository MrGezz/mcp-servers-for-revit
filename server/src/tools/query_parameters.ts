import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerQueryParametersTool(server: McpServer) {
  server.tool(
    "query_parameters",
    "List every parameter of one element: name, value, storage type, read-only flag. Use before set_parameters to learn exact parameter names.",
    { elementId: ElementId },
    async (args) => callRevit("query_parameters", { elementId: args.elementId })
  );
}
