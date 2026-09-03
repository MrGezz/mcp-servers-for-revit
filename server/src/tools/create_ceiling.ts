import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateCeilingTool(server: McpServer) {
  server.tool(
    "create_ceiling",
    "Creates one or more ceilings from boundary points (mm). Needs a plan view. Ceiling type by name or typeId (see get_available_family_types). Returns new element ids.",
    {
      data: z.array(z.object({
        boundaryPoints: z.array(Pt).describe("Closed boundary polygon"),
        level: z.number().describe("Base level elevation; snapped to nearest project level"),
        levelOffset: z.number().optional().describe("Offset from level (mm)"),
        typeId: z.number().optional().describe("Ceiling type ElementId"),
        ceilingType: z.string().optional().describe("Ceiling type name"),
      })),
    },
    async (args) => callRevit("create_ceiling", args)
  );
}
