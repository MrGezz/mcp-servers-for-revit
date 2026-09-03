import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { fail } from "../utils/reply.js";

export function registerCreateRampTool(server: McpServer) {
  server.tool(
    "create_ramp",
    "Ramp creation is not supported. The Revit API (2022-2027) exposes no public ramp-creation surface. Use the Revit UI to create ramps.",
    {},
    async (_args) => fail("Ramp creation is not supported by the Revit API (2022-2027). Use the Revit UI to create ramps.")
  );
}
