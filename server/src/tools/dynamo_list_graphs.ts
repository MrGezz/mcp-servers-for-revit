import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ok, fail, errorMessage } from "../utils/reply.js";
import { listGraphFiles, readGraph } from "../dynamo/DynGraph.js";

export function registerDynamoListGraphsTool(server: McpServer) {
  server.tool(
    "dynamo_list_graphs",
    "List Dynamo graphs (.dyn) and custom nodes (.dyf) under a folder. Revit does not need to be running. With describe:true, also returns name, description, node count, and package dependencies.",
    {
      folder: z.string().describe("Absolute path to the folder to scan."),
      recursive: z.boolean().optional().describe("Descend into subfolders. Default true."),
      describe: z
        .boolean()
        .optional()
        .describe("Include name, description, nodes, packages. Default false."),
      limit: z.number().optional().describe("Maximum files to return. Default 200."),
    },
    async (args) => {
      try {
        const limit = args.limit ?? 200;
        const files = await listGraphFiles(args.folder, args.recursive !== false, limit);

        if (files.length === 0) {
          return ok(`No .dyn or .dyf files found under ${args.folder}.`);
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
            lines.push(`[!!]  ${f.name} — could not be read: ${errorMessage(error)}`);
            lines.push(`      ${f.path}`);
          }
        }

        if (files.length >= limit) {
          lines.push("", `Stopped at the limit of ${limit}. Raise "limit" to see more.`);
        }

        return ok(lines.join("\n"));
      } catch (error) {
        return fail(`dynamo_list_graphs failed: ${errorMessage(error)}`);
      }
    }
  );
}
