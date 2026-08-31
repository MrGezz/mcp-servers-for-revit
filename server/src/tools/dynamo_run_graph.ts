import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { access } from "node:fs/promises";
import { dynamoOp } from "../dynamo/backend.js";

export function registerDynamoRunGraphTool(server: McpServer) {
  server.tool(
    "dynamo_run_graph",
    "Open a Dynamo graph in the running Revit and, optionally, RUN it. " +
      "Running a graph modifies the model and CANNOT be undone by this tool: a Dynamo graph opens and " +
      "commits its own transactions, so there is no dry run and no rollback. " +
      "Requires confirm:true to run. Call dynamo_status first.",
    {
      path: z.string().describe("Absolute path to the .dyn file to open."),
      run: z
        .boolean()
        .optional()
        .describe("Run the graph after opening it. Default false — open only, which is always safe."),
      confirm: z
        .boolean()
        .optional()
        .describe("Must be true to run. Ignored when run is false."),
      timeout_seconds: z
        .number()
        .optional()
        .describe("How long to wait for the run. Default 300. A graph runs on Revit's API thread and blocks the UI."),
    },
    async (args) => {
      try {
        // Fail on a bad path here rather than letting Revit raise it: the error
        // is clearer, and it costs nothing.
        try {
          await access(args.path);
        } catch {
          throw new Error(`No file at ${args.path}.`);
        }
        if (!/\.dyn$/i.test(args.path)) {
          throw new Error(
            `${args.path} is not a .dyn file. A .dyf is a custom node definition and cannot be run directly.`
          );
        }

        if (args.run && args.confirm !== true) {
          return {
            content: [
              {
                type: "text",
                text:
                  `REFUSED: running a Dynamo graph is not reversible.\n\n` +
                  `A graph manages its own Revit transactions, so this tool cannot wrap it in one and roll ` +
                  `it back — whatever the graph writes to the model, stays. There is deliberately no dry run.\n\n` +
                  `Re-issue with confirm:true if that is intended. To inspect the graph first without ` +
                  `touching the model, use dynamo_read_graph, or call this tool with run:false to open it ` +
                  `in Dynamo and look at it.`,
              },
            ],
          };
        }

        const timeoutMs = Math.min(Math.max(args.timeout_seconds ?? 300, 5), 900) * 1000;
        const lines: string[] = [];

        const opened = await dynamoOp("open", { path: args.path }, timeoutMs);
        lines.push(`## open (via ${opened.backend} backend)`);
        lines.push(JSON.stringify(opened.data, null, 2));

        if (!opened.ok) {
          lines.push("", "Open did not report success, so the graph was NOT run.");
          return { content: [{ type: "text", text: lines.join("\n") }] };
        }

        if (!args.run) {
          lines.push("", "Opened only — not run (pass run:true and confirm:true to run it).");
          return { content: [{ type: "text", text: lines.join("\n") }] };
        }

        const ran = await dynamoOp("run", { path: args.path, confirm: true }, timeoutMs);
        lines.push("", `## run (via ${ran.backend} backend)`);
        lines.push(JSON.stringify(ran.data, null, 2));

        return { content: [{ type: "text", text: lines.join("\n") }] };
      } catch (error) {
        return {
          content: [
            { type: "text", text: `dynamo_run_graph failed: ${error instanceof Error ? error.message : String(error)}` },
          ],
        };
      }
    }
  );
}
