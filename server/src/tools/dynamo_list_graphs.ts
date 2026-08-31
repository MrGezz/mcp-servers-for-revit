import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { listGraphFiles, readGraph } from "../dynamo/DynGraph.js";

export function registerDynamoListGraphsTool(server: McpServer) {
  server.tool(
    "dynamo_list_graphs",
    "List the Dynamo graphs (.dyn) and custom nodes (.dyf) under a folder, optionally with each " +
      "graph's name, description, node count and package dependencies. Use this to answer " +
      "'which of my graphs already does X' before writing a new one. Revit does not need to be running.",
    {
      folder: z.string().describe("Absolute path to the folder to scan."),
      recursive: z.boolean().optional().describe("Descend into subfolders. Default true."),
      describe: z
        .boolean()
        .optional()
        .describe(
          "Open each graph to report its name, description, node count and packages. " +
            "Slower on large folders; default false."
        ),
      limit: z.number().optional().describe("Maximum files to return. Default 200."),
    },
    async (args) => {
      try {
        const limit = args.limit ?? 200;
        const files = await listGraphFiles(args.folder, args.recursive !== false, limit);

        if (files.length === 0) {
          return {
            content: [{ type: "text", text: `No .dyn or .dyf files found under ${args.folder}.` }],
          };
        }

        const lines: string[] = [`${files.length} file(s) under ${args.folder}`, ""];

        for (const f of files) {
          if (!args.describe) {
            lines.push(`${f.kind === "custom-node" ? "[dyf]" : "[dyn]"} ${f.name}   ${f.bytes} bytes   ${f.modified.slice(0, 10)}`);
            lines.push(`      ${f.path}`);
            continue;
          }

          // A graph that fails to parse is REPORTED, not skipped. A folder scan
          // that quietly omits the broken file is how a corrupt graph goes
          // unnoticed for months.
          try {
            const loaded = await readGraph(f.path);
            const doc = loaded.doc;
            const nodeCount = Array.isArray(doc.Nodes) ? doc.Nodes.length : 0;
            const packages = (doc.NodeLibraryDependencies ?? []).map((d) => String(d.Name ?? "?"));
            lines.push(`${f.kind === "custom-node" ? "[dyf]" : "[dyn]"} ${doc.Name || f.name}`);
            lines.push(`      ${f.path}`);
            if (doc.Description) lines.push(`      ${String(doc.Description).split("\n")[0].slice(0, 160)}`);
            lines.push(
              `      ${nodeCount} nodes · packages: ${packages.length ? packages.join(", ") : "none"}`
            );
          } catch (error) {
            lines.push(`[!!]  ${f.name} — could not be read: ${error instanceof Error ? error.message : String(error)}`);
            lines.push(`      ${f.path}`);
          }
        }

        if (files.length >= limit) {
          lines.push("", `Stopped at the limit of ${limit}. Raise "limit" to see more.`);
        }

        return { content: [{ type: "text", text: lines.join("\n") }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `dynamo_list_graphs failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
        };
      }
    }
  );
}
