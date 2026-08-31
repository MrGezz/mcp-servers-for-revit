import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerAnalyzeModelStatisticsTool(server: McpServer) {
  server.tool(
    "analyze_model_statistics",
    "Analyze model complexity: total elements, types, families, views, sheets, per-category counts and " +
      "level-by-level distribution. The per-type breakdown inside each category is OMITTED by default - " +
      "it was 95.2% of a 181,717-character response on a real model and overflowed the client limit. Pass " +
      "includeDetailedTypes to get it. The type and family COUNTS are always reported.",
    {
      includeDetailedTypes: z
        .boolean()
        .optional()
        .default(false)
        .describe(
          "Include the per-family/per-type breakdown within each category. Defaults to FALSE: on a real " +
            "model this was 1,042 entries and 95.2% of the whole response."
        ),
    },
    async (args, extra) => {
      const params = {
        includeDetailedTypes: args.includeDetailedTypes ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("analyze_model_statistics", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Analyze model statistics failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
