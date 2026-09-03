import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateRoofTool(server: McpServer) {
  server.tool(
    "create_roof",
    "Creates footprint or extrusion roofs (mm). `type` is a Revit RoofType name (e.g. 'Generic - 400mm'). Needs an active project with levels. Returns new element ids.",
    {
      data: z.array(z.object({
        type: z.string().describe("Revit RoofType name, e.g. 'Generic - 400mm'"),
        level: z.number().describe("Base elevation (mm)"),
        height: z.number().optional().describe("Ridge height above level (mm)"),
        slope: z.number().optional().describe("Slope angle (degrees)"),
        options: z.object({
          width: z.number().optional().describe("Roof width (mm); default 30"),
          length: z.number().optional().describe("Roof length (mm); default 30"),
          shape: z.enum(["footprint", "extrusion"]).optional().describe("Roof shape; default footprint"),
          referencePlaneId: z.number().int().optional().describe("Reference plane id (required for extrusion)"),
          extrusionStart: z.number().optional().describe("Extrusion start offset (mm); default 0"),
          extrusionEnd: z.number().optional().describe("Extrusion end offset (mm); default length"),
          typeId: z.number().int().optional().describe("Element id of a specific RoofType"),
        }).optional().describe("Dimensions, shape, and type selection"),
      })),
    },
    async (args) => callRevit("create_roof", args)
  );
}
