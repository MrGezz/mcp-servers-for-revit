import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { readGraph } from "../dynamo/DynGraph.js";
import { summarize, renderSummary } from "../dynamo/summarize.js";

export function registerDynamoReadGraphTool(server: McpServer) {
  server.tool(
    "dynamo_read_graph",
    "Read a Dynamo graph (.dyn) or custom node (.dyf) and explain what it does: every node with " +
      "what feeds it and what it feeds, the Python and Code Block bodies in full, the Dynamo Player " +
      "inputs, the package dependencies it needs to run, and any structural problems. " +
      "Works entirely from the file — Revit does not need to be running.",
    {
      path: z.string().describe("Absolute path to the .dyn or .dyf file."),
      include_code: z
        .boolean()
        .optional()
        .describe("Include Python and Code Block bodies. Default true; set false for a shorter structural overview."),
      max_nodes: z
        .number()
        .optional()
        .describe("Cap on how many nodes to detail. Default 200."),
      format: z
        .enum(["text", "json"])
        .optional()
        .describe("'text' (default) is compact and readable; 'json' returns the full structured summary."),
    },
    async (args) => {
      try {
        const loaded = await readGraph(args.path);
        const summary = summarize(loaded.doc, loaded.filePath, { includeCode: args.include_code !== false });

        const body =
          args.format === "json"
            ? JSON.stringify(summary, null, 2)
            : renderSummary(summary, { maxNodes: args.max_nodes ?? 200 });

        return { content: [{ type: "text", text: body }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `dynamo_read_graph failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
