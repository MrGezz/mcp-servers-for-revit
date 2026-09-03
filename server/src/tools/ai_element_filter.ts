import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerAIElementFilterTool(server: McpServer) {
  server.tool(
    "ai_element_filter",
    "Query elements project-wide by category, class name, family symbol and/or visibility, optionally narrowed by a bounding box (mm; needs boundingBoxMin AND boundingBoxMax and at least one other filter). Returns id, name, family, category and key parameters, capped by maxElements (default 50).",
    {
      data: z.object({
        filterCategory: z
          .string()
          .optional()
          .describe("OST_Walls, OST_Floors, OST_GenericModel, etc."),
        filterElementType: z
          .string()
          .optional()
          .describe("Class name, e.g. 'Wall' or 'Autodesk.Revit.DB.Wall'"),
        filterFamilySymbolId: z
          .number()
          .optional()
          .describe("FamilySymbol ElementId; use -1 to skip"),
        includeTypes: z
          .boolean()
          .default(false),
        includeInstances: z
          .boolean()
          .default(true),
        filterVisibleInCurrentView: z
          .boolean()
          .optional()
          .describe("Limit to elements visible in current view (instances only)"),
        boundingBoxMin: Pt.optional(),
        boundingBoxMax: Pt.optional(),
        maxElements: z
          .number()
          .optional()
          .describe("Max elements returned; default 50"),
      }),
    },
    async (args) => callRevit("ai_element_filter", args)
  );
}
