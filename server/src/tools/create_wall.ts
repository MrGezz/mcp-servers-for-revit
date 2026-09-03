import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateWallTool(server: McpServer) {
  server.tool(
    "create_wall",
    "Create straight or curved walls (mm). Each wall needs start/end points, height and the base level elevation; wall type by name or typeId (see get_available_family_types with OST_Walls). Returns the new element ids.",
    {
      data: z
        .array(
          z.object({
            startPoint: Pt,
            endPoint: Pt,
            midPoint: Pt.optional().describe("Arc mid-point for curved walls (mm); omit for straight walls"),
            height: z.number().describe("mm"),
            thickness: z.number().optional().describe("mm; ignored when the type fixes it"),
            baseLevel: z.number().describe("Base level elevation, mm"),
            baseOffset: z.number().optional().describe("mm"),
            topConstraintType: z.number().int().optional().describe("0/omit for unconnected height (default), 1 to constrain to a top level (requires topLevelId)"),
            topLevelId: z.number().int().optional(),
            topOffset: z.number().optional().describe("mm"),
            wallType: z.string().optional().describe("Wall type name"),
            typeId: z.number().int().optional().describe("Wall type ElementId (preferred over wallType)"),
            isStructural: z.boolean().optional().describe("Defaults to true (structural) when omitted"),
            flipped: z.boolean().optional(),
          })
        )
        .min(1),
    },
    async (args) => callRevit("create_wall", args)
  );
}
