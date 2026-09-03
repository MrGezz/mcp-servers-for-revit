import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerSetParametersTool(server: McpServer) {
  server.tool(
    "set_parameters",
    "Set named parameters on one Revit element by id. Length values in mm. Returns success; if any parameters were skipped (not found or read-only), lists them with the reason in the message.",
    {
      elementId: ElementId,
      parameters: z.record(z.union([z.string(), z.number(), z.boolean()])).describe("Parameter names mapped to their new values"),
    },
    async (args) => callRevit("set_parameters", args)
  );
}
