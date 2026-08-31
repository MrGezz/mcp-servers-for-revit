/**
 * A reader and writer for Dynamo `.dyn` / `.dyf` graph files.
 *
 * ---------------------------------------------------------------------------
 * WHY THIS IS NOT A MODEL
 * ---------------------------------------------------------------------------
 * A `.dyn` is JSON with two parallel halves that must stay in step:
 *
 *   Nodes[] / Connectors[]            the program
 *   View.NodeViews[] / View.Annotations[]   where it sits on the canvas
 *
 * The tempting design is to parse into a typed model, mutate that, and
 * serialise it back. Every tool that has tried it drops whatever the model does
 * not know about - most visibly the entire `View` block, which turns a laid-out
 * graph into a pile of nodes stacked at the origin, and less visibly
 * `Bindings`, `ExtensionWorkspaceData` and `Linting`, whose loss is silent.
 *
 * So this module never rebuilds a document. It parses to a plain object, mutates
 * that object in place, and writes it back. Keys it has never heard of survive
 * by construction rather than by being enumerated. `readGraph` -> `writeGraph`
 * with no edits in between is byte-identical for any file Dynamo itself wrote,
 * and `selfTest.ts` asserts exactly that against real graphs.
 *
 * ---------------------------------------------------------------------------
 * PORT IDENTITY
 * ---------------------------------------------------------------------------
 * Connectors do NOT reference nodes. They reference PORT ids:
 *
 *   { "Start": "<an output port Id>", "End": "<an input port Id>" }
 *
 * so wiring is always node -> port index -> port Id. Callers name ports by index
 * or by name and this module resolves them; a caller that hands us a port id
 * directly is also accepted, because that is what a reader of the raw file has.
 */

import { readFile, writeFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { randomUUID } from "node:crypto";

// ---------------------------------------------------------------------------
// Shapes. Deliberately loose: `[key: string]: unknown` on every record is the
// mechanism that preserves fields this file has never seen.
// ---------------------------------------------------------------------------

export interface DynPort {
  Id: string;
  Name?: string;
  Description?: string;
  UsingDefaultValue?: boolean;
  [key: string]: unknown;
}

export interface DynNode {
  Id: string;
  NodeType?: string;
  ConcreteType?: string;
  Inputs?: DynPort[];
  Outputs?: DynPort[];
  /** Present on CodeBlockNode and PythonScriptNode. */
  Code?: string;
  /** PythonScriptNode: "PythonNet3" | "CPython3" | "IronPython2" | ... */
  Engine?: string;
  /** FunctionNode, e.g. "Revit.Elements.Category.ByName@string". */
  FunctionSignature?: string;
  InputValue?: unknown;
  Replication?: string;
  Description?: string;
  [key: string]: unknown;
}

export interface DynConnector {
  Start: string;
  End: string;
  Id: string;
  IsHidden?: string;
  [key: string]: unknown;
}

export interface DynNodeView {
  Id: string;
  Name?: string;
  X?: number;
  Y?: number;
  IsSetAsInput?: boolean;
  IsSetAsOutput?: boolean;
  Excluded?: boolean;
  ShowGeometry?: boolean;
  [key: string]: unknown;
}

export interface DynView {
  Dynamo?: Record<string, unknown>;
  Camera?: Record<string, unknown>;
  ConnectorPins?: unknown[];
  NodeViews?: DynNodeView[];
  Annotations?: unknown[];
  X?: number;
  Y?: number;
  Zoom?: number;
  [key: string]: unknown;
}

export interface DynDocument {
  Uuid?: string;
  Name?: string;
  Description?: string;
  IsCustomNode?: boolean;
  Nodes?: DynNode[];
  Connectors?: DynConnector[];
  /** Dynamo Player inputs. Each Id is a NODE id, not a port id. */
  Inputs?: Array<Record<string, unknown>>;
  Outputs?: Array<Record<string, unknown>>;
  NodeLibraryDependencies?: Array<Record<string, unknown>>;
  View?: DynView;
  [key: string]: unknown;
}

/** A graph plus everything needed to write it back exactly as it was found. */
export interface LoadedGraph {
  filePath: string;
  doc: DynDocument;
  /** Byte length of the file as read. Used to report the size of a rewrite. */
  originalBytes: number;
  /** True when the source file ended with a newline, so a rewrite can match. */
  trailingNewline: boolean;
  /** "CRLF" | "LF" | "NONE" — preserved across a rewrite. */
  eol: "CRLF" | "LF" | "NONE" | "MIXED";
  /**
   * True when this runtime can preserve numeric literals exactly, so a no-edit
   * rewrite is byte-identical. False on Node 20/21, where a whole-number double
   * loses its trailing ".0" — harmless to Dynamo, noisy in a diff.
   */
  numberFidelity: boolean;
}

// ---------------------------------------------------------------------------
// Number fidelity
// ---------------------------------------------------------------------------
//
// Dynamo is a .NET application and Newtonsoft writes a `double` with its decimal
// point intact: `"ScaleFactor": 1.0`, `"EyeX": -17.0`, `"InputValue": 1000.0`.
// JavaScript has one number type, so `JSON.parse` turns all of those into 1, -17
// and 1000, and `JSON.stringify` writes them back WITHOUT the `.0`.
//
// Nothing breaks — Newtonsoft reads `1` into a double perfectly well. But on a
// real graph this rewrites a few hundred lines that nobody asked to change, so a
// one-word edit to a Python node arrives as a 200-line diff. For a tool whose
// entire job is editing files that live in version control, that is a defect.
//
// Node 22 added source-text access in the `JSON.parse` reviver, which lets the
// original literal be captured exactly. Where it exists, numbers are preserved
// verbatim and a no-edit round trip is byte-identical. Where it does not (Node
// 20 and 21, both still supported by this package), behaviour is unchanged and
// `numberFidelity` on the loaded graph reports `false` so callers can say so
// rather than quietly claiming a guarantee they do not have.

/** A number whose exact source text must survive serialisation. */
class RawNumber {
  constructor(readonly source: string, readonly value: number) {}
  toJSON(): string {
    return `${RAW_PREFIX}${this.source}${RAW_SUFFIX}`;
  }
}

// The sentinel carries a token generated once per process, built from
// characters JSON.stringify never escapes. A fixed marker could in principle
// collide with a string the document itself contains; a random one cannot.
//
// Escaping matters here: a control character would be emitted as \u0001, which
// would no longer match the restore pattern, and the sentinel would be written
// into the user's graph as literal text. Plain ASCII only.
const RAW_TOKEN = randomUUID().replace(/-/g, "");
const RAW_PREFIX = `@@N${RAW_TOKEN}:`;
const RAW_SUFFIX = `:${RAW_TOKEN}N@@`;

/** Does this runtime expose the original literal to a JSON.parse reviver? */
export const NUMBER_FIDELITY_AVAILABLE: boolean = (() => {
  try {
    let sawSource = false;
    JSON.parse("1.0", function (_key: string, value: unknown, context?: { source?: string }) {
      if (context && typeof context.source === "string") sawSource = true;
      return value;
    } as never);
    return sawSource;
  } catch {
    return false;
  }
})();

function parsePreservingNumbers(text: string): { doc: DynDocument; fidelity: boolean } {
  if (!NUMBER_FIDELITY_AVAILABLE) {
    return { doc: JSON.parse(text) as DynDocument, fidelity: false };
  }
  const doc = JSON.parse(text, function (_key: string, value: unknown, context?: { source?: string }) {
    // Only wrap when the literal would NOT survive a plain round trip. Wrapping
    // every number would work but would put a sentinel through the serialiser
    // thousands of times per file for no gain.
    if (typeof value === "number" && context && typeof context.source === "string") {
      if (context.source !== String(value)) return new RawNumber(context.source, value);
    }
    return value;
  } as never) as DynDocument;
  return { doc, fidelity: true };
}

/** Replace the serialiser's sentinels with the original numeric literals. */
function restoreRawNumbers(text: string): string {
  if (!text.includes(RAW_PREFIX)) return text;
  // The sentinel is emitted as a JSON string, so the quotes come off with it.
  const pattern = new RegExp(`"${RAW_PREFIX}(-?[0-9eE.+-]+)${RAW_SUFFIX}"`, "g");
  return text.replace(pattern, "$1");
}

/**
 * Read a preserved number back as a plain number.
 * Callers that do arithmetic on graph values need the value, not the wrapper.
 */
export function numberOf(value: unknown): number | undefined {
  if (typeof value === "number") return value;
  if (value instanceof RawNumber) return value.value;
  return undefined;
}

// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

function detectEol(text: string): LoadedGraph["eol"] {
  let crlf = 0;
  let lf = 0;
  for (let i = 0; i < text.length; i++) {
    if (text.charCodeAt(i) === 10) {
      if (i > 0 && text.charCodeAt(i - 1) === 13) crlf++;
      else lf++;
    }
  }
  if (crlf > 0 && lf === 0) return "CRLF";
  if (lf > 0 && crlf === 0) return "LF";
  if (crlf === 0 && lf === 0) return "NONE";
  return "MIXED";
}

export async function readGraph(filePath: string): Promise<LoadedGraph> {
  const raw = await readFile(filePath, "utf8");
  // A BOM would otherwise land inside the first JSON key and fail the parse
  // with a message that names the wrong problem.
  const text = raw.charCodeAt(0) === 0xfeff ? raw.slice(1) : raw;

  let doc: DynDocument;
  let numberFidelity: boolean;
  try {
    const parsed = parsePreservingNumbers(text);
    doc = parsed.doc;
    numberFidelity = parsed.fidelity;
  } catch (error) {
    throw new Error(
      `${path.basename(filePath)} is not valid JSON, so it is not a Dynamo graph this tool can read ` +
        `(${error instanceof Error ? error.message : String(error)}). ` +
        `Dynamo 1.x used an XML .dyn format which is not supported.`
    );
  }

  if (typeof doc !== "object" || doc === null || !Array.isArray(doc.Nodes)) {
    throw new Error(
      `${path.basename(filePath)} parsed as JSON but has no "Nodes" array, so it is not a Dynamo 2.x graph.`
    );
  }

  return {
    filePath,
    doc,
    originalBytes: Buffer.byteLength(raw, "utf8"),
    trailingNewline: /\r?\n$/.test(text),
    eol: detectEol(text),
    numberFidelity,
  };
}

// ---------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------

/**
 * Serialise a document the way Dynamo does: two-space indent, LF, no trailing
 * newline unless the original had one.
 *
 * Matching Dynamo's own formatting matters because these files live in git.
 * A writer that reformats turns a one-node edit into a whole-file diff.
 */
export function serializeGraph(loaded: LoadedGraph): string {
  let text = restoreRawNumbers(JSON.stringify(loaded.doc, null, 2));

  // A surviving sentinel would mean a preserved literal reached the output in a
  // position the restore pattern does not match, which would write sentinel text
  // into the user's graph. Refuse rather than write it.
  if (text.includes(RAW_PREFIX)) {
    throw new Error(
      "Internal error: a preserved numeric literal was not restored. The graph was NOT written."
    );
  }

  if (loaded.eol === "CRLF") text = text.split("\n").join("\r\n");
  if (loaded.trailingNewline) text += loaded.eol === "CRLF" ? "\r\n" : "\n";
  return text;
}

export async function writeGraph(loaded: LoadedGraph, destination?: string): Promise<{ path: string; bytes: number }> {
  const target = destination ?? loaded.filePath;
  const text = serializeGraph(loaded);
  await writeFile(target, text, "utf8");
  return { path: target, bytes: Buffer.byteLength(text, "utf8") };
}

// ---------------------------------------------------------------------------
// Lookup
// ---------------------------------------------------------------------------

export function nodes(doc: DynDocument): DynNode[] {
  return Array.isArray(doc.Nodes) ? doc.Nodes : [];
}

export function connectors(doc: DynDocument): DynConnector[] {
  return Array.isArray(doc.Connectors) ? doc.Connectors : [];
}

export function nodeViews(doc: DynDocument): DynNodeView[] {
  const v = doc.View;
  return v && Array.isArray(v.NodeViews) ? v.NodeViews : [];
}

/**
 * Resolve a node by id, by the display name it carries in the View, or by a
 * unique case-insensitive substring of either.
 *
 * Node ids are 32 hex characters, which nobody wants to type. But names are not
 * unique, so an ambiguous name is an ERROR that lists the candidates rather than
 * a silent pick of the first — picking silently is how an edit lands on the
 * wrong node and is only noticed after it has been saved.
 */
export function findNode(doc: DynDocument, selector: string): DynNode {
  const all = nodes(doc);
  const views = new Map(nodeViews(doc).map((v) => [v.Id, v]));

  const exactId = all.find((n) => n.Id === selector);
  if (exactId) return exactId;

  const label = (n: DynNode): string => String(views.get(n.Id)?.Name ?? n.ConcreteType ?? "");

  const exactName = all.filter((n) => label(n) === selector);
  if (exactName.length === 1) return exactName[0];
  if (exactName.length > 1) {
    throw new Error(
      `"${selector}" matches ${exactName.length} nodes by name. Use an id instead: ` +
        exactName.map((n) => n.Id).join(", ")
    );
  }

  const needle = selector.toLowerCase();
  const fuzzy = all.filter((n) => label(n).toLowerCase().includes(needle) || n.Id.startsWith(needle));
  if (fuzzy.length === 1) return fuzzy[0];
  if (fuzzy.length > 1) {
    throw new Error(
      `"${selector}" is ambiguous — ${fuzzy.length} nodes match. Candidates: ` +
        fuzzy.slice(0, 8).map((n) => `${label(n)} (${n.Id})`).join("; ")
    );
  }

  throw new Error(`No node in this graph matches "${selector}".`);
}

/** Resolve a port to its Id, accepting a port id, a port name, or an index. */
export function resolvePort(node: DynNode, side: "in" | "out", selector: string | number): string {
  const ports = (side === "in" ? node.Inputs : node.Outputs) ?? [];
  if (ports.length === 0) {
    throw new Error(`Node ${node.Id} has no ${side === "in" ? "input" : "output"} ports.`);
  }

  if (typeof selector === "number") {
    const port = ports[selector];
    if (!port) {
      throw new Error(
        `Node ${node.Id} has ${ports.length} ${side === "in" ? "input" : "output"} port(s); ` +
          `index ${selector} is out of range.`
      );
    }
    return port.Id;
  }

  const byId = ports.find((p) => p.Id === selector);
  if (byId) return byId.Id;

  const byName = ports.filter((p) => p.Name === selector);
  if (byName.length === 1) return byName[0].Id;
  if (byName.length > 1) {
    throw new Error(`Node ${node.Id} has ${byName.length} ${side} ports named "${selector}". Use an index.`);
  }

  throw new Error(
    `Node ${node.Id} has no ${side} port "${selector}". Available: ` +
      ports.map((p, i) => `${i}:${p.Name ?? "(unnamed)"}`).join(", ")
  );
}

// ---------------------------------------------------------------------------
// Mutation
// ---------------------------------------------------------------------------

/** Dynamo writes ids as 32 lowercase hex characters with no dashes. */
export function newId(): string {
  return randomUUID().replace(/-/g, "");
}

/**
 * Connect one node's output to another node's input.
 *
 * Dynamo allows an output to fan out to many inputs but allows an input exactly
 * ONE incoming connector. Adding a second silently produces a graph Dynamo will
 * repair on load by discarding one of them, so the existing connector is removed
 * here and reported, rather than leaving the caller to discover it later.
 */
export function connect(
  doc: DynDocument,
  from: { node: string; port?: string | number },
  to: { node: string; port?: string | number }
): { connectorId: string; replaced: string | null } {
  const source = findNode(doc, from.node);
  const target = findNode(doc, to.node);
  const startId = resolvePort(source, "out", from.port ?? 0);
  const endId = resolvePort(target, "in", to.port ?? 0);

  if (!Array.isArray(doc.Connectors)) doc.Connectors = [];

  let replaced: string | null = null;
  const occupied = doc.Connectors.findIndex((c) => c.End === endId);
  if (occupied >= 0) {
    replaced = doc.Connectors[occupied].Id;
    doc.Connectors.splice(occupied, 1);
  }

  const connectorId = newId();
  doc.Connectors.push({ Start: startId, End: endId, Id: connectorId, IsHidden: "False" });
  return { connectorId, replaced };
}

/** Remove every connector attached to a port, a node, or a specific connector id. */
export function disconnect(doc: DynDocument, selector: { node?: string; port?: string | number; connectorId?: string }): number {
  if (!Array.isArray(doc.Connectors)) return 0;
  const before = doc.Connectors.length;

  if (selector.connectorId) {
    doc.Connectors = doc.Connectors.filter((c) => c.Id !== selector.connectorId);
    return before - doc.Connectors.length;
  }

  if (!selector.node) throw new Error("disconnect needs either a connectorId or a node.");
  const node = findNode(doc, selector.node);

  let portIds: Set<string>;
  if (selector.port === undefined) {
    portIds = new Set([...(node.Inputs ?? []), ...(node.Outputs ?? [])].map((p) => p.Id));
  } else {
    // A named port can exist on both sides; take whichever resolves.
    const ids: string[] = [];
    for (const side of ["in", "out"] as const) {
      try { ids.push(resolvePort(node, side, selector.port)); } catch { /* not on this side */ }
    }
    if (ids.length === 0) throw new Error(`Node ${node.Id} has no port "${selector.port}".`);
    portIds = new Set(ids);
  }

  doc.Connectors = doc.Connectors.filter((c) => !portIds.has(c.Start) && !portIds.has(c.End));
  return before - doc.Connectors.length;
}

/**
 * Set the body of a Python node or the expression of a Code Block.
 *
 * Refuses a node that carries no `Code` field at all, because assigning one to
 * (say) a FunctionNode produces a file Dynamo loads without complaint and
 * without the code having any effect.
 */
export function setCode(doc: DynDocument, selector: string, code: string): DynNode {
  const node = findNode(doc, selector);
  if (typeof node.Code !== "string") {
    throw new Error(
      `Node ${node.Id} is a ${node.NodeType ?? "node"} and has no Code field. ` +
        `Only CodeBlockNode and PythonScriptNode carry code.`
    );
  }
  node.Code = code;
  return node;
}

/** Set the Python engine on a Python node. */
export function setEngine(doc: DynDocument, selector: string, engine: string): DynNode {
  const node = findNode(doc, selector);
  if (node.NodeType !== "PythonScriptNode") {
    throw new Error(`Node ${node.Id} is a ${node.NodeType ?? "node"}, not a PythonScriptNode.`);
  }
  node.Engine = engine;
  return node;
}

/** Move a node on the canvas. Positions live in View.NodeViews, not on the node. */
export function moveNode(doc: DynDocument, selector: string, x: number, y: number): DynNodeView {
  const node = findNode(doc, selector);
  const view = nodeViews(doc).find((v) => v.Id === node.Id);
  if (!view) throw new Error(`Node ${node.Id} has no NodeView entry, so it has no canvas position to move.`);
  view.X = x;
  view.Y = y;
  return view;
}

/** Rename a node's canvas label. */
export function renameNode(doc: DynDocument, selector: string, name: string): DynNodeView {
  const node = findNode(doc, selector);
  const view = nodeViews(doc).find((v) => v.Id === node.Id);
  if (!view) throw new Error(`Node ${node.Id} has no NodeView entry, so it has no name to change.`);
  view.Name = name;
  return view;
}

/**
 * Set the value of an input node (number, string, boolean).
 *
 * Also updates the matching Dynamo Player entry in the top-level `Inputs` array
 * when there is one. Those two places hold the same value and Player reads the
 * second; changing only the node leaves Player showing the old default.
 */
export function setInputValue(doc: DynDocument, selector: string, value: unknown): { node: string; playerEntryUpdated: boolean } {
  const node = findNode(doc, selector);
  if (!("InputValue" in node)) {
    throw new Error(
      `Node ${node.Id} is a ${node.NodeType ?? "node"} and has no InputValue. ` +
        `Use set_code for a Code Block, or pick an input node.`
    );
  }
  node.InputValue = value;

  let playerEntryUpdated = false;
  if (Array.isArray(doc.Inputs)) {
    for (const entry of doc.Inputs) {
      if (entry && entry.Id === node.Id) {
        entry.Value = typeof value === "string" ? value : String(value);
        playerEntryUpdated = true;
      }
    }
  }
  return { node: node.Id, playerEntryUpdated };
}

/**
 * Remove a node, its connectors, its NodeView and any Dynamo Player entry.
 *
 * Deleting the node alone leaves dangling connectors pointing at port ids that
 * no longer exist, which Dynamo reports as a corrupt graph on open.
 */
export function removeNode(doc: DynDocument, selector: string): { id: string; connectorsRemoved: number } {
  const node = findNode(doc, selector);
  const connectorsRemoved = disconnect(doc, { node: node.Id });

  doc.Nodes = nodes(doc).filter((n) => n.Id !== node.Id);
  if (doc.View && Array.isArray(doc.View.NodeViews)) {
    doc.View.NodeViews = doc.View.NodeViews.filter((v) => v.Id !== node.Id);
  }
  if (Array.isArray(doc.Inputs)) {
    doc.Inputs = doc.Inputs.filter((e) => !e || e.Id !== node.Id);
  }
  return { id: node.Id, connectorsRemoved };
}

/**
 * Add a Code Block node, wired to nothing, at a given canvas position.
 *
 * A Code Block is the one node type that can be authored correctly without a
 * node catalogue: its ports are derived by Dynamo from the expression itself, so
 * we can create it with zero inputs and let Dynamo work the rest out on load.
 * Anything else would need the exact ConcreteType and port set of a real library
 * node, which is a lookup this file deliberately does not pretend to have.
 */
export function addCodeBlock(doc: DynDocument, code: string, position?: { x: number; y: number }): DynNode {
  const id = newId();
  const outputId = newId();

  const node: DynNode = {
    ConcreteType: "Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore",
    Id: id,
    NodeType: "CodeBlockNode",
    Inputs: [],
    Outputs: [
      {
        Id: outputId,
        Name: "",
        Description: "Value of expression at line 1",
        UsingDefaultValue: false,
        Level: 2,
        UseLevels: false,
        KeepListStructure: false,
      },
    ],
    Replication: "Disabled",
    Description: "Allows for DesignScript code to be authored directly",
    Code: code,
  };

  if (!Array.isArray(doc.Nodes)) doc.Nodes = [];
  doc.Nodes.push(node);

  if (!doc.View) doc.View = {};
  if (!Array.isArray(doc.View.NodeViews)) doc.View.NodeViews = [];
  doc.View.NodeViews.push({
    Id: id,
    Name: "Code Block",
    IsSetAsInput: false,
    IsSetAsOutput: false,
    Excluded: false,
    ShowGeometry: true,
    X: position?.x ?? 0,
    Y: position?.y ?? 0,
  });

  return node;
}

/**
 * Add a Python node with a body and an engine.
 *
 * `inputCount` sets how many IN[] ports the node exposes; Dynamo names them
 * IN[0], IN[1] and so on, which is what the body indexes.
 */
export function addPythonNode(
  doc: DynDocument,
  code: string,
  options?: { engine?: string; inputCount?: number; position?: { x: number; y: number } }
): DynNode {
  const id = newId();
  const inputCount = Math.max(0, options?.inputCount ?? 1);

  const node: DynNode = {
    ConcreteType: "PythonNodeModels.PythonNode, PythonNodeModels",
    Code: code,
    Engine: options?.engine ?? "CPython3",
    VariableInputPorts: true,
    Id: id,
    NodeType: "PythonScriptNode",
    Inputs: Array.from({ length: inputCount }, (_, i) => ({
      Id: newId(),
      Name: `IN[${i}]`,
      Description: "Input #" + i,
      UsingDefaultValue: false,
      Level: 2,
      UseLevels: false,
      KeepListStructure: false,
    })),
    Outputs: [
      {
        Id: newId(),
        Name: "OUT",
        Description: "Result of the python script",
        UsingDefaultValue: false,
        Level: 2,
        UseLevels: false,
        KeepListStructure: false,
      },
    ],
    Replication: "Disabled",
    Description: "Runs an embedded Python script.",
  };

  if (!Array.isArray(doc.Nodes)) doc.Nodes = [];
  doc.Nodes.push(node);

  if (!doc.View) doc.View = {};
  if (!Array.isArray(doc.View.NodeViews)) doc.View.NodeViews = [];
  doc.View.NodeViews.push({
    Id: id,
    Name: "Python Script",
    IsSetAsInput: false,
    IsSetAsOutput: false,
    Excluded: false,
    ShowGeometry: true,
    X: options?.position?.x ?? 0,
    Y: options?.position?.y ?? 0,
  });

  return node;
}

// ---------------------------------------------------------------------------
// Integrity
// ---------------------------------------------------------------------------

export interface GraphProblem {
  severity: "error" | "warning";
  kind: string;
  detail: string;
}

/**
 * Check the invariants Dynamo relies on but the file format does not enforce.
 *
 * Run after every edit. Every one of these is a state that produces either a
 * "graph is corrupt" dialog on open or a silently wrong canvas, and none of them
 * is detectable by JSON validity.
 */
export function validate(doc: DynDocument): GraphProblem[] {
  const problems: GraphProblem[] = [];
  const allNodes = nodes(doc);
  const nodeIds = new Set(allNodes.map((n) => n.Id));

  const portOwner = new Map<string, string>();
  for (const n of allNodes) {
    for (const p of [...(n.Inputs ?? []), ...(n.Outputs ?? [])]) {
      if (portOwner.has(p.Id)) {
        problems.push({
          severity: "error",
          kind: "duplicate-port-id",
          detail: `Port id ${p.Id} is used by both ${portOwner.get(p.Id)} and ${n.Id}.`,
        });
      }
      portOwner.set(p.Id, n.Id);
    }
  }

  const duplicateNodeIds = allNodes.map((n) => n.Id).filter((id, i, a) => a.indexOf(id) !== i);
  for (const id of new Set(duplicateNodeIds)) {
    problems.push({ severity: "error", kind: "duplicate-node-id", detail: `Node id ${id} appears more than once.` });
  }

  const inputUse = new Map<string, number>();
  for (const c of connectors(doc)) {
    if (!portOwner.has(c.Start)) {
      problems.push({
        severity: "error",
        kind: "dangling-connector",
        detail: `Connector ${c.Id} starts at port ${c.Start}, which belongs to no node.`,
      });
    }
    if (!portOwner.has(c.End)) {
      problems.push({
        severity: "error",
        kind: "dangling-connector",
        detail: `Connector ${c.Id} ends at port ${c.End}, which belongs to no node.`,
      });
    }
    inputUse.set(c.End, (inputUse.get(c.End) ?? 0) + 1);
  }

  for (const [portId, count] of inputUse) {
    if (count > 1) {
      problems.push({
        severity: "error",
        kind: "multiply-connected-input",
        detail: `Input port ${portId} has ${count} incoming connectors; Dynamo permits exactly one.`,
      });
    }
  }

  const viewed = new Set(nodeViews(doc).map((v) => v.Id));
  for (const id of nodeIds) {
    if (!viewed.has(id)) {
      problems.push({
        severity: "warning",
        kind: "missing-nodeview",
        detail: `Node ${id} has no View.NodeViews entry, so it will open at the canvas origin.`,
      });
    }
  }
  for (const id of viewed) {
    if (!nodeIds.has(id)) {
      problems.push({
        severity: "warning",
        kind: "orphan-nodeview",
        detail: `View.NodeViews references node ${id}, which no longer exists.`,
      });
    }
  }

  for (const entry of doc.Inputs ?? []) {
    const id = entry && (entry.Id as string | undefined);
    if (id && !nodeIds.has(id)) {
      problems.push({
        severity: "warning",
        kind: "orphan-player-input",
        detail: `Dynamo Player input references node ${id}, which no longer exists.`,
      });
    }
  }

  return problems;
}

// ---------------------------------------------------------------------------
// Enumeration
// ---------------------------------------------------------------------------

export interface GraphFileEntry {
  path: string;
  name: string;
  bytes: number;
  modified: string;
  kind: "graph" | "custom-node";
}

/** List `.dyn` and `.dyf` files under a directory. */
export async function listGraphFiles(root: string, recursive = true, limit = 500): Promise<GraphFileEntry[]> {
  const out: GraphFileEntry[] = [];

  async function walk(dir: string, depth: number): Promise<void> {
    if (out.length >= limit || depth > 12) return;
    let entries;
    try {
      entries = await readdir(dir, { withFileTypes: true });
    } catch {
      return; // unreadable directory is not a reason to abandon the whole scan
    }
    for (const e of entries) {
      if (out.length >= limit) return;
      const p = path.join(dir, e.name);
      if (e.isDirectory()) {
        if (recursive && !e.name.startsWith(".")) await walk(p, depth + 1);
      } else if (/\.dy[nf]$/i.test(e.name)) {
        const s = await stat(p);
        out.push({
          path: p,
          name: e.name,
          bytes: s.size,
          modified: s.mtime.toISOString(),
          kind: /\.dyf$/i.test(e.name) ? "custom-node" : "graph",
        });
      }
    }
  }

  await walk(root, 0);
  out.sort((a, b) => a.path.localeCompare(b.path));
  return out;
}
