import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateEquipmentTool(server: McpServer) {
  server.tool(
    "create_equipment",
    "Place MEP equipment (mechanical, electrical, plumbing) in the active model (mm). Requires location and baseLevel per item; rotation, category, family, and typeId are optional. Returns created element ids.",
    {
      data: z.array(z.object({
        location: Pt,
        rotation: z.number().optional().describe("Degrees, around Z-axis"),
        baseLevel: z.number(),
        baseOffset: z.number().optional().describe("Additional vertical offset in mm above baseLevel"),
        category: z.string().optional().describe("E.g. Mechanical Equipment, Electrical Equipment"),
        equipmentType: z.string().optional(),
        familyName: z.string().optional(),
        typeId: z.number().optional(),
      })),
    },
    async (args) => callRevit("create_equipment", args)
  );
}
