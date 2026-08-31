import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { copyFile } from "node:fs/promises";
import {
  readGraph,
  writeGraph,
  validate,
  connect,
  disconnect,
  setCode,
  setEngine,
  setInputValue,
  moveNode,
  renameNode,
  removeNode,
  addCodeBlock,
  addPythonNode,
  GraphProblem,
} from "../dynamo/DynGraph.js";

/**
 * One flat operation shape with optional fields, rather than a discriminated
 * union.
 *
 * A union would model this more precisely, but PR #9 in this repo exists because
 * some MCP clients (VS Code among them) choke on the JSON Schema the SDK emits
 * for richer zod constructs. A flat object with a string `op` and optional
 * fields produces a schema every client understands, and the per-op requirements
 * are enforced in code below with errors that say what was missing.
 */
const OperationSchema = z.object({
  op: z
    .string()
    .describe(
      "One of: set_code, set_engine, set_input_value, connect, disconnect, add_code_block, " +
        "add_python_node, remove_node, move_node, rename_node, set_graph_name, set_description"
    ),
  node: z.string().optional().describe("Target node: its id, its canvas name, or a unique substring of either."),
  from_node: z.string().optional().describe("connect: the source node."),
  from_port: z.union([z.string(), z.number()]).optional().describe("connect: source output port, by name or index. Default 0."),
  to_node: z.string().optional().describe("connect: the destination node."),
  to_port: z.union([z.string(), z.number()]).optional().describe("connect: destination input port, by name or index. Default 0."),
  connector_id: z.string().optional().describe("disconnect: remove one specific connector by id."),
  port: z.union([z.string(), z.number()]).optional().describe("disconnect: limit removal to this port of the node."),
  code: z.string().optional().describe("set_code / add_code_block / add_python_node: the body or expression."),
  engine: z.string().optional().describe("set_engine / add_python_node: CPython3, PythonNet3 or IronPython2."),
  input_count: z.number().optional().describe("add_python_node: how many IN[] ports to expose. Default 1."),
  value: z.union([z.string(), z.number(), z.boolean()]).optional().describe("set_input_value: the new value."),
  name: z.string().optional().describe("rename_node / set_graph_name: the new name."),
  description: z.string().optional().describe("set_description: the new graph description."),
  x: z.number().optional().describe("Canvas X for a new or moved node."),
  y: z.number().optional().describe("Canvas Y for a new or moved node."),
});

type Operation = z.infer<typeof OperationSchema>;

/** Assert a per-op required field, naming the op and the field when it is missing. */
function need<T>(value: T | undefined, opName: string, field: string): T {
  if (value === undefined || value === null || value === "") {
    throw new Error(`Operation "${opName}" requires "${field}".`);
  }
  return value;
}

export function registerDynamoEditGraphTool(server: McpServer) {
  server.tool(
    "dynamo_edit_graph",
    "Edit a Dynamo graph (.dyn) file: rewrite Python or Code Block bodies, rewire nodes, change " +
      "input values, add or remove nodes, and rename or reposition them. Applies a list of operations " +
      "in order, checks the result for structural corruption, backs up the original, and writes the " +
      "file back preserving everything it did not change — including the canvas layout. " +
      "Revit does not need to be running.",
    {
      path: z.string().describe("Absolute path to the .dyn or .dyf file to edit."),
      operations: z.array(OperationSchema).describe("Operations to apply, in order."),
      output_path: z
        .string()
        .optional()
        .describe("Write the result here instead of over the original. The original is then left untouched."),
      dry_run: z
        .boolean()
        .optional()
        .describe("Apply the operations and report what would change without writing anything. Default false."),
    },
    async (args) => {
      try {
        const loaded = await readGraph(args.path);
        const before = validate(loaded.doc);
        const log: string[] = [];

        for (const [index, raw] of args.operations.entries()) {
          const o = raw as Operation;
          const where = `operation ${index + 1} (${o.op})`;
          try {
            switch (o.op) {
              case "set_code": {
                const node = setCode(loaded.doc, need(o.node, o.op, "node"), need(o.code, o.op, "code"));
                log.push(`${where}: replaced the code on ${node.Id} (${node.NodeType}).`);
                break;
              }
              case "set_engine": {
                const node = setEngine(loaded.doc, need(o.node, o.op, "node"), need(o.engine, o.op, "engine"));
                log.push(`${where}: set engine on ${node.Id} to ${node.Engine}.`);
                break;
              }
              case "set_input_value": {
                const r = setInputValue(loaded.doc, need(o.node, o.op, "node"), need(o.value, o.op, "value"));
                log.push(
                  `${where}: set value on ${r.node}` +
                    (r.playerEntryUpdated ? " and updated its Dynamo Player entry." : ".")
                );
                break;
              }
              case "connect": {
                const r = connect(
                  loaded.doc,
                  { node: need(o.from_node, o.op, "from_node"), port: o.from_port },
                  { node: need(o.to_node, o.op, "to_node"), port: o.to_port }
                );
                log.push(
                  `${where}: connected ${o.from_node} -> ${o.to_node} (connector ${r.connectorId})` +
                    (r.replaced
                      ? `, replacing connector ${r.replaced} — that input already had a source, and Dynamo allows only one.`
                      : ".")
                );
                break;
              }
              case "disconnect": {
                const removed = disconnect(loaded.doc, {
                  node: o.node,
                  port: o.port,
                  connectorId: o.connector_id,
                });
                log.push(`${where}: removed ${removed} connector(s).`);
                break;
              }
              case "add_code_block": {
                const node = addCodeBlock(loaded.doc, need(o.code, o.op, "code"), { x: o.x ?? 0, y: o.y ?? 0 });
                log.push(`${where}: added Code Block ${node.Id} at (${o.x ?? 0}, ${o.y ?? 0}).`);
                break;
              }
              case "add_python_node": {
                const node = addPythonNode(loaded.doc, need(o.code, o.op, "code"), {
                  engine: o.engine,
                  inputCount: o.input_count,
                  position: { x: o.x ?? 0, y: o.y ?? 0 },
                });
                log.push(
                  `${where}: added Python node ${node.Id} (${node.Engine}, ${(node.Inputs ?? []).length} inputs) ` +
                    `at (${o.x ?? 0}, ${o.y ?? 0}).`
                );
                break;
              }
              case "remove_node": {
                const r = removeNode(loaded.doc, need(o.node, o.op, "node"));
                log.push(`${where}: removed node ${r.id} and ${r.connectorsRemoved} attached connector(s).`);
                break;
              }
              case "move_node": {
                const v = moveNode(loaded.doc, need(o.node, o.op, "node"), need(o.x, o.op, "x"), need(o.y, o.op, "y"));
                log.push(`${where}: moved ${v.Id} to (${v.X}, ${v.Y}).`);
                break;
              }
              case "rename_node": {
                const v = renameNode(loaded.doc, need(o.node, o.op, "node"), need(o.name, o.op, "name"));
                log.push(`${where}: renamed ${v.Id} to "${v.Name}".`);
                break;
              }
              case "set_graph_name": {
                loaded.doc.Name = need(o.name, o.op, "name");
                log.push(`${where}: graph name is now "${loaded.doc.Name}".`);
                break;
              }
              case "set_description": {
                loaded.doc.Description = need(o.description, o.op, "description");
                log.push(`${where}: description updated.`);
                break;
              }
              default:
                throw new Error(
                  `Unknown operation "${o.op}". Known operations: set_code, set_engine, set_input_value, ` +
                    `connect, disconnect, add_code_block, add_python_node, remove_node, move_node, ` +
                    `rename_node, set_graph_name, set_description.`
                );
            }
          } catch (error) {
            // Stop at the first failure and report how far it got. Applying the
            // rest would write a half-executed edit list, which is harder to
            // reason about than nothing having happened.
            throw new Error(
              `${where} failed: ${error instanceof Error ? error.message : String(error)}\n\n` +
                (log.length ? `Applied before the failure (NOT written to disk):\n${log.join("\n")}` : "Nothing was applied.")
            );
          }
        }

        const after = validate(loaded.doc);
        const newErrors = after.filter(
          (p: GraphProblem) => p.severity === "error" && !before.some((b) => b.kind === p.kind && b.detail === p.detail)
        );

        if (newErrors.length) {
          throw new Error(
            `These operations would corrupt the graph, so nothing was written:\n` +
              newErrors.map((p) => `  ${p.kind}: ${p.detail}`).join("\n")
          );
        }

        const lines = [...log];
        const warnings = after.filter((p) => p.severity === "warning");
        if (warnings.length) {
          lines.push("", `Warnings (${warnings.length}):`, ...warnings.map((p) => `  ${p.kind}: ${p.detail}`));
        }

        if (args.dry_run) {
          lines.push("", "dry_run: nothing was written.");
          return { content: [{ type: "text", text: lines.join("\n") }] };
        }

        const target = args.output_path ?? loaded.filePath;
        let backup: string | null = null;
        if (!args.output_path) {
          // Overwriting an authored graph without a backup is not recoverable.
          backup = `${loaded.filePath}.bak`;
          await copyFile(loaded.filePath, backup);
        }

        const written = await writeGraph(loaded, target);
        lines.push(
          "",
          `Wrote ${written.bytes} bytes to ${written.path} (was ${loaded.originalBytes}).`,
          backup ? `Original backed up to ${backup}.` : "Original left untouched."
        );

        return { content: [{ type: "text", text: lines.join("\n") }] };
      } catch (error) {
        return {
          content: [
            { type: "text", text: `dynamo_edit_graph failed: ${error instanceof Error ? error.message : String(error)}` },
          ],
        };
      }
    }
  );
}
