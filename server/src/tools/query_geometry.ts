import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerQueryGeometryTool(server: McpServer) {
  server.tool(
    "query_geometry",
    "Returns bounding box (MinMm/MaxMm), solid count and face details for one element. viewId and detailLevel are optional. Areas in m2 (AreaM2, SurfaceAreaM2), volumes in m3 (VolumeM3).",
    {
      elementId: ElementId,
      viewId: ElementId.optional().describe("View for geometry computation"),
      detailLevel: z.number().int().optional().describe("1=Coarse, 2=Medium, 3=Fine"),
    },
    async (args) => {
      const params: Record<string, unknown> = { elementId: args.elementId };
      if (args.viewId !== undefined) params.viewId = args.viewId;
      if (args.detailLevel !== undefined) params.detailLevel = args.detailLevel;
      return callRevit("query_geometry", params);
    }
  );
}
