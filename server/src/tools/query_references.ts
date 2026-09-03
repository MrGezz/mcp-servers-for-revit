import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerQueryReferencesTool(server: McpServer) {
  server.tool(
    "query_references",
    "Get stable geometric references of a Revit element for dimensioning and tagging. Returns face references (AreaM2, in m2) and edge references (LengthMm, in mm).",
    {
      elementId: ElementId,
    },
    async (args) => callRevit("query_references", { elementId: args.elementId })
  );
}
