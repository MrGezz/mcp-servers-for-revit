import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateGridTool(server: McpServer) {
  server.tool(
    "create_grid",
    "Create a grid system (mm) in Revit. Requires an active view. Returns created grid element ids.",
    {
      xCount: z.number().int().positive(),
      xSpacing: z.number().positive(),
      xStartLabel: z.string().default("A"),
      xNamingStyle: z.enum(["alphabetic", "numeric"]).default("alphabetic"),
      yCount: z.number().int().positive(),
      ySpacing: z.number().positive(),
      yStartLabel: z.string().default("1"),
      yNamingStyle: z.enum(["alphabetic", "numeric"]).default("numeric"),
      xExtentMin: z.number().default(0).describe("Min X; where Y grids start"),
      xExtentMax: z.number().default(50000).describe("Max X; where Y grids end"),
      yExtentMin: z.number().default(0).describe("Min Y; where X grids start"),
      yExtentMax: z.number().default(50000).describe("Max Y; where X grids end"),
      elevation: z.number().default(0),
      xStartPosition: z.number().default(0),
      yStartPosition: z.number().default(0),
    },
    async (args) => callRevit("create_grid", args)
  );
}
