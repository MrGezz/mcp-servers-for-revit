import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerAnalyzeModelStatisticsTool(server: McpServer) {
  server.tool(
    "analyze_model_statistics",
    "Returns element counts, families, views, sheets, and per-category/level distribution. Per-type breakdown is omitted by default (can be 95%+ of payload); pass includeDetailedTypes=true to include it.",
    {
      includeDetailedTypes: z
        .boolean()
        .optional()
        .default(false)
        .describe("Include per-family/per-type breakdown within each category"),
    },
    async (args) => callRevit("analyze_model_statistics", { includeDetailedTypes: args.includeDetailedTypes ?? false })
  );
}
