import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateDirectShapeTool(server: McpServer) {
  server.tool(
    "create_direct_shape",
    "Create DirectShape solids (Box, Cylinder, Extrusion) in Revit (mm). Returns created element ids.",
    {
      data: z.array(z.object({
        shapeType: z.enum(["Box", "Cylinder", "Extrusion"]),
        width: z.number().optional(),
        depth: z.number().optional(),
        height: z.number().optional(),
        radius: z.number().optional(),
        center: Pt.optional(),
        points: z.array(Pt).optional().describe("Profile vertices for extrusion"),
        extrusionDir: Pt.optional().describe("Extrusion direction vector"),
        extrusionLength: z.number().optional(),
        category: z.string().optional(),
        material: z.string().optional(),
      })),
    },
    async (args) => callRevit("create_direct_shape", args)
  );
}
