import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateModelCurveTool(server: McpServer) {
  server.tool(
    "create_model_curve",
    "Create model curves (line, arc, circle, or spline) in the active Revit model (mm). Requires an active model view. Returns created element ids.",
    {
      data: z.array(z.object({
        points: z.array(Pt).describe("Points defining curve geometry (mm)"),
        curveType: z.string().optional().describe("Line (2pts), Arc (3pts), Circle (center+radius), or Spline (2+pts)"),
        center: Pt.optional().describe("Center point for Circle (mm)"),
        radius: z.number().optional().describe("Radius for Circle (mm)"),
        normal: Pt.optional().describe("Normal vector for Circle plane"),
        sketchPlaneId: z.number().optional().describe("SketchPlane element ID; omit to auto-create"),
      })),
    },
    async (args) => callRevit("create_model_curve", args)
  );
}
