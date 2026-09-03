import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateOpeningTool(server: McpServer) {
  server.tool(
    "create_opening",
    "Create openings (mm) in a host element. Supports Wall, Floor, and Roof types. hostElementId from get_current_view_elements. Returns the new element ids.",
    {
      data: z.array(z.object({
        hostElementId: z.number().describe("Host element id"),
        openingType: z.string().optional().describe("Wall | Floor | Roof (Shaft not functional)"),
        location: Pt.describe("Opening location"),
        width: z.number(),
        height: z.number(),
        sillHeight: z.number().optional().describe("Sill height above location for wall openings (mm)"),
      })),
    },
    async (args) => callRevit("create_opening", args)
  );
}
