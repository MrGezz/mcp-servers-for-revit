import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreatePointBasedElementTool(server: McpServer) {
  server.tool(
    "create_point_based_element",
    "Place point-based family instances (doors, windows, furniture, generic models) in mm. Give typeId (from get_available_family_types) or a category so a type can be found. Doors/windows need a host wall (auto-detected unless hostWallId). Returns the new element ids; nothing placed is an error.",
    {
      data: z
        .array(
          z.object({
            category: z.string().optional().describe("OST_Doors, OST_Windows, OST_Furniture...; used to pick a type when typeId is omitted"),
            typeId: z.number().optional().describe("Family type ElementId (preferred)"),
            locationPoint: Pt,
            height: z.number().describe("mm"),
            baseLevel: z.number().describe("Base level elevation, mm"),
            baseOffset: z.number().describe("mm"),
            rotation: z.number().optional().describe("Degrees, non-hosted elements only"),
            hostWallId: z.number().optional().describe("Wall ElementId for doors/windows"),
            facingFlipped: z.boolean().optional().default(false),
          })
        )
        .min(1),
    },
    async (args) => callRevit("create_point_based_element", args)
  );
}
