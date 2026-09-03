import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { copyFile, access } from "node:fs/promises";
import {
  readGraph,
  newGraph,
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
  addFunctionNode,
  GraphProblem,
} from "../dynamo/DynGraph.js";
import { ok, fail, errorMessage } from "../utils/reply.js";

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
        "add_python_node, add_function_node, remove_node, move_node, rename_node, set_graph_name, set_description"
    ),
  node: z.string().optional().describe("Target node: id, canvas name, or unique substring."),
  from_node: z.string().optional().describe("connect: source node."),
  from_port: z.union([z.string(), z.number()]).optional().describe("connect: source output port, name or index."),
  to_node: z.string().optional().describe("connect: destination node."),
  to_port: z.union([z.string(), z.number()]).optional().describe("connect: destination input port, name or index."),
  connector_id: z.string().optional().describe("disconnect: specific connector id to remove."),
  port: z.union([z.string(), z.number()]).optional().describe("disconnect: limit removal to this port."),
  code: z.string().optional().describe("set_code / add_code_block / add_python_node body."),
  engine: z.string().optional().describe("CPython3, PythonNet3, or IronPython2."),
  input_count: z.number().optional().describe("add_python_node: number of IN[] ports (at least 1)."),
  function_signature: z
    .string()
    .optional()
    .describe(
      "add_function_node: a library node by its DesignScript signature, e.g. " +
        "DSOffice.Data.OpenXMLExportExcel@string,string,var[][],int,int,bool,bool (dynamo_read_graph format:json shows them)."
    ),
  inputs: z.array(z.string()).optional().describe("add_function_node: input port names, one per signature argument."),
  outputs: z.array(z.string()).optional().describe("add_function_node: output port names (default one, 'var')."),
  defaults: z.array(z.string()).optional().describe("add_function_node: inputs left at the function's default value."),
  value: z.union([z.string(), z.number(), z.boolean()]).optional().describe("set_input_value: the new value."),
  name: z.string().optional().describe("rename_node / set_graph_name: the new name."),
  description: z.string().optional().describe("set_description: new graph description."),
  x: z.number().optional(),
  y: z.number().optional(),
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
    "Edit a Dynamo .dyn/.dyf file without Revit running, or create one with create:true. Applies operations (add_function_node for any library node, add_python_node, add_code_block, connect, set_code, move_node, etc.) in order, validates for structural corruption, backs up the original, and writes the result. Returns a log of applied changes.",
    {
      path: z.string().describe("Absolute path to the .dyn or .dyf file to edit."),
      operations: z.array(OperationSchema).describe("Operations to apply, in order."),
      output_path: z
        .string()
        .optional()
        .describe("Write here instead of over the original."),
      dry_run: z
        .boolean()
        .optional()
        .describe("Validate without writing. Default false."),
      create: z
        .boolean()
        .optional()
        .describe("If the file does not exist, start from a new empty Dynamo 3 graph instead of failing. Default false."),
    },
    async (args) => {
      try {
        // A missing file is only a graph-to-be when the caller said so; a typo
        // in a path must still fail loudly rather than quietly create a file.
        const exists = await access(args.path).then(() => true, () => false);
        if (!exists && !args.create) {
          throw new Error(`${args.path} does not exist. Pass create:true to start a new graph there.`);
        }
        const created = !exists;
        const loaded = created ? newGraph(args.path) : await readGraph(args.path);
        const before = validate(loaded.doc);
        const log: string[] = [];
        if (created) log.push(`created a new empty graph "${loaded.doc.Name}" (Dynamo 3 format).`);

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
              case "add_function_node": {
                const node = addFunctionNode(loaded.doc, need(o.function_signature, o.op, "function_signature"), {
                  inputs: need(o.inputs, o.op, "inputs"),
                  outputs: o.outputs,
                  defaults: o.defaults,
                  name: o.name,
                  position: { x: o.x ?? 0, y: o.y ?? 0 },
                });
                log.push(
                  `${where}: added ${node.FunctionSignature} as node ${node.Id} ` +
                    `(${(node.Inputs ?? []).length} inputs, ${(node.Outputs ?? []).length} outputs) at (${o.x ?? 0}, ${o.y ?? 0}).`
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
                    `connect, disconnect, add_code_block, add_python_node, add_function_node, remove_node, move_node, ` +
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
          return ok(lines.join("\n"));
        }

        const target = args.output_path ?? loaded.filePath;
        let backup: string | null = null;
        if (!args.output_path && !created) {
          // Overwriting an authored graph without a backup is not recoverable.
          backup = `${loaded.filePath}.bak`;
          await copyFile(loaded.filePath, backup);
        }

        const written = await writeGraph(loaded, target);
        lines.push(
          "",
          created
            ? `Wrote ${written.bytes} bytes to ${written.path} (new file).`
            : `Wrote ${written.bytes} bytes to ${written.path} (was ${loaded.originalBytes}).`,
          created ? "" : backup ? `Original backed up to ${backup}.` : "Original left untouched."
        );

        return ok(lines.join("\n"));
      } catch (error) {
        return fail(`dynamo_edit_graph failed: ${errorMessage(error)}`);
      }
    }
  );
}
