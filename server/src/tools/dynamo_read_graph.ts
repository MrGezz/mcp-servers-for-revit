import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { readGraph } from "../dynamo/DynGraph.js";
import { summarize, renderSummary } from "../dynamo/summarize.js";
import { ok, fail, errorMessage } from "../utils/reply.js";

export function registerDynamoReadGraphTool(server: McpServer) {
  server.tool(
    "dynamo_read_graph",
    "Read a .dyn or .dyf file and return a summary: node graph, Python/Code Block bodies, Dynamo Player inputs, package dependencies, structural issues. No Revit connection needed.",
    {
      path: z.string().describe("Absolute path to the .dyn or .dyf file."),
      include_code: z
        .boolean()
        .optional()
        .describe("Include Python/Code Block bodies (default true)."),
      max_nodes: z
        .number()
        .optional()
        .describe("Cap on nodes to detail (default 200)."),
      format: z
        .enum(["text", "json"])
        .optional()
        .describe("'text' readable summary; 'json' full structure."),
    },
    async (args) => {
      try {
        const loaded = await readGraph(args.path);
        const summary = summarize(loaded.doc, loaded.filePath, { includeCode: args.include_code !== false });

        const body =
          args.format === "json"
            ? summary
            : renderSummary(summary, { maxNodes: args.max_nodes ?? 200 });

        return ok(body);
      } catch (error) {
        return fail(`dynamo_read_graph failed: ${errorMessage(error)}`);
      }
    }
  );
}
