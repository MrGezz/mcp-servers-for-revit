import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateMEPCurveTool(server: McpServer) {
  server.tool(
    "create_mep_curve",
    "Creates a duct, pipe, or conduit run between two points (mm). level is an elevation in mm; the nearest level becomes the reference level. systemType applies to ducts only. Returns the new element id.",
    {
      mepType: z.enum(["duct", "pipe", "conduit"]).describe("duct, pipe, or conduit"),
      start: Pt,
      end: Pt,
      level: z.number().describe("Elevation (mm) of the reference level; nearest level is used"),
      diameter: z.number().optional().default(200),
      systemType: z.string().optional().describe("System type name (ducts only)"),
    },
    async (args) => callRevit("create_mep_curve", args)
  );
}
