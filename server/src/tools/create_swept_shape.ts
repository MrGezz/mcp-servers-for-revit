import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateSweptShapeTool(server: McpServer) {
  server.tool(
    "create_swept_shape",
    "Create swept solid shapes along a path (mm). Supports Rect, Circle, and Horseshoe section profiles. Returns created element ids.",
    {
      data: z.array(z.object({
        sectionType: z.enum(["Rect", "Circle", "Horseshoe"]),
        width: z.number().optional().describe("Section width (Rect/Horseshoe)"),
        height: z.number().optional().describe("Section height (Rect/Horseshoe)"),
        radius: z.number().optional().describe("Circle section radius"),
        pathPoints: z.array(Pt).describe("Sweep path points"),
        category: z.string().optional().describe("Revit category name"),
      })).describe("Swept shapes to create"),
    },
    async (args) => callRevit("create_swept_shape", args)
  );
}
