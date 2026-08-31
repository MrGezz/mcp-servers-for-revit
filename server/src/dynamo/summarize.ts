/**
 * Turn a parsed `.dyn` into something an assistant can reason about.
 *
 * The raw file is the wrong thing to hand a model: a 90 KB graph is ~2,500 lines
 * of JSON in which the interesting content — what the graph DOES — is spread
 * across three arrays joined by 32-character GUIDs. Dumping it wastes most of a
 * context window and still leaves the reader doing port-id joins by hand.
 *
 * So the summary resolves the joins once: every node is reported with the nodes
 * feeding it and the nodes it feeds, by NAME. Code bodies are included because
 * they are the part a reader actually needs to change, and they are the part no
 * amount of node-name reading can reconstruct.
 */

import {
  DynDocument,
  DynNode,
  nodes,
  connectors,
  nodeViews,
  validate,
  GraphProblem,
} from "./DynGraph.js";

export interface NodeSummary {
  id: string;
  name: string;
  type: string;
  /** Present for FunctionNode: the DesignScript signature it calls. */
  signature?: string;
  /** Present for PythonScriptNode. */
  engine?: string;
  /** Present for CodeBlockNode and PythonScriptNode. */
  code?: string;
  value?: unknown;
  inputs: Array<{ port: string; from: string | null }>;
  outputs: Array<{ port: string; to: string[] }>;
  position?: { x: number; y: number };
}

export interface GraphSummary {
  name: string;
  description: string;
  file: string;
  dynamoVersion: string | null;
  runType: string | null;
  isCustomNode: boolean;
  counts: {
    nodes: number;
    connectors: number;
    byType: Record<string, number>;
  };
  /** Packages this graph needs. An empty list means it runs on stock Dynamo. */
  packages: Array<{ name: string; version: string; nodeCount: number }>;
  /** Dynamo Player inputs, in the order Player shows them. */
  playerInputs: Array<{ node: string; name: string; type: string; value: unknown }>;
  /** Nodes with no incoming and no outgoing connectors — usually leftovers. */
  orphans: string[];
  problems: GraphProblem[];
  nodes: NodeSummary[];
}

function labelOf(doc: DynDocument): Map<string, string> {
  const views = nodeViews(doc);
  const map = new Map<string, string>();
  for (const v of views) if (v.Name) map.set(v.Id, String(v.Name));
  return map;
}

/**
 * Strip the assembly half of a ConcreteType.
 * "DSRevitNodesUI.ElementsOfCategoryInView, DSRevitNodesUI" -> the first half,
 * which is the part that identifies the node.
 */
function shortType(node: DynNode): string {
  if (node.NodeType === "FunctionNode" && node.FunctionSignature) {
    return String(node.FunctionSignature).split("@")[0];
  }
  const concrete = String(node.ConcreteType ?? node.NodeType ?? "unknown");
  return concrete.split(",")[0];
}

export function summarize(doc: DynDocument, file: string, options?: { includeCode?: boolean; codeLimit?: number }): GraphSummary {
  const includeCode = options?.includeCode !== false;
  const codeLimit = options?.codeLimit ?? 4000;

  const allNodes = nodes(doc);
  const allConnectors = connectors(doc);
  const labels = labelOf(doc);
  const views = new Map(nodeViews(doc).map((v) => [v.Id, v]));

  // Resolve the port-id joins once, rather than per node.
  const portToNode = new Map<string, { nodeId: string; portName: string }>();
  for (const n of allNodes) {
    for (const p of n.Inputs ?? []) portToNode.set(p.Id, { nodeId: n.Id, portName: String(p.Name ?? "") });
    for (const p of n.Outputs ?? []) portToNode.set(p.Id, { nodeId: n.Id, portName: String(p.Name ?? "") });
  }

  const displayName = (id: string): string => labels.get(id) ?? shortType(allNodes.find((n) => n.Id === id) ?? ({ Id: id } as DynNode));

  /** endPortId -> the node feeding it */
  const feeder = new Map<string, string>();
  /** startPortId -> the nodes it feeds */
  const consumers = new Map<string, string[]>();
  for (const c of allConnectors) {
    const src = portToNode.get(c.Start);
    const dst = portToNode.get(c.End);
    if (src) feeder.set(c.End, src.nodeId);
    if (dst) {
      const list = consumers.get(c.Start) ?? [];
      list.push(dst.nodeId);
      consumers.set(c.Start, list);
    }
  }

  const byType: Record<string, number> = {};
  const nodeSummaries: NodeSummary[] = [];
  const orphans: string[] = [];

  for (const n of allNodes) {
    const type = String(n.NodeType ?? "unknown");
    byType[type] = (byType[type] ?? 0) + 1;

    const inputs = (n.Inputs ?? []).map((p) => {
      const from = feeder.get(p.Id);
      return { port: String(p.Name ?? ""), from: from ? displayName(from) : null };
    });
    const outputs = (n.Outputs ?? []).map((p) => ({
      port: String(p.Name ?? ""),
      to: (consumers.get(p.Id) ?? []).map(displayName),
    }));

    const wired = inputs.some((i) => i.from !== null) || outputs.some((o) => o.to.length > 0);
    if (!wired) orphans.push(displayName(n.Id));

    const view = views.get(n.Id);
    const summary: NodeSummary = {
      id: n.Id,
      name: labels.get(n.Id) ?? shortType(n),
      type,
      inputs,
      outputs,
    };
    if (n.FunctionSignature) summary.signature = String(n.FunctionSignature);
    if (n.Engine) summary.engine = String(n.Engine);
    if (includeCode && typeof n.Code === "string") {
      summary.code =
        n.Code.length > codeLimit
          ? n.Code.slice(0, codeLimit) + `\n… [${n.Code.length - codeLimit} more characters]`
          : n.Code;
    }
    if ("InputValue" in n) summary.value = n.InputValue;
    if (view && typeof view.X === "number" && typeof view.Y === "number") {
      summary.position = { x: view.X, y: view.Y };
    }
    nodeSummaries.push(summary);
  }

  const packages = (doc.NodeLibraryDependencies ?? []).map((d) => ({
    name: String(d.Name ?? "unknown"),
    version: String(d.Version ?? ""),
    nodeCount: Array.isArray(d.Nodes) ? d.Nodes.length : 0,
  }));

  const playerInputs = (doc.Inputs ?? []).map((e) => ({
    node: String(e.Id ?? ""),
    name: String(e.Name ?? ""),
    type: String(e.Type ?? e.Type2 ?? ""),
    value: e.Value,
  }));

  const view = doc.View ?? {};
  const dynamoBlock = (view.Dynamo ?? {}) as Record<string, unknown>;

  return {
    name: String(doc.Name ?? ""),
    description: String(doc.Description ?? ""),
    file,
    dynamoVersion: dynamoBlock.Version ? String(dynamoBlock.Version) : null,
    runType: dynamoBlock.RunType ? String(dynamoBlock.RunType) : null,
    isCustomNode: Boolean(doc.IsCustomNode),
    counts: { nodes: allNodes.length, connectors: allConnectors.length, byType },
    packages,
    playerInputs,
    orphans,
    problems: validate(doc),
    nodes: nodeSummaries,
  };
}

/**
 * Render a summary as text.
 *
 * JSON is the honest transport but a 60-node graph reads far better as an
 * indented list, and the token cost is roughly half.
 */
export function renderSummary(s: GraphSummary, options?: { maxNodes?: number }): string {
  const maxNodes = options?.maxNodes ?? 200;
  const lines: string[] = [];

  lines.push(`# ${s.name || "(unnamed graph)"}${s.isCustomNode ? "  [custom node .dyf]" : ""}`);
  lines.push(s.file);
  if (s.description) lines.push(`\n${s.description}`);

  lines.push(
    `\nAuthored with Dynamo ${s.dynamoVersion ?? "(unknown)"} · run type ${s.runType ?? "(unknown)"} · ` +
      `${s.counts.nodes} nodes, ${s.counts.connectors} connectors`
  );
  const typeList = Object.entries(s.counts.byType)
    .sort((a, b) => b[1] - a[1])
    .map(([t, c]) => `${t} ×${c}`)
    .join(", ");
  if (typeList) lines.push(typeList);

  if (s.packages.length) {
    lines.push(`\n## Package dependencies (${s.packages.length})`);
    lines.push(
      "This graph will NOT run on a machine without these installed.\n" +
        s.packages.map((p) => `  - ${p.name} ${p.version} (${p.nodeCount} nodes)`).join("\n")
    );
  } else {
    lines.push("\n## Package dependencies\n  none — runs on stock Dynamo.");
  }

  if (s.playerInputs.length) {
    lines.push(`\n## Dynamo Player inputs (${s.playerInputs.length})`);
    for (const p of s.playerInputs) {
      lines.push(`  - ${p.name} [${p.type}] = ${JSON.stringify(p.value)}   (node ${p.node})`);
    }
  }

  if (s.problems.length) {
    lines.push(`\n## Problems (${s.problems.length})`);
    for (const p of s.problems) lines.push(`  ${p.severity.toUpperCase()} ${p.kind}: ${p.detail}`);
  }

  if (s.orphans.length) {
    lines.push(`\n## Unwired nodes (${s.orphans.length})`);
    lines.push("  " + s.orphans.join(", "));
  }

  lines.push(`\n## Nodes`);
  for (const n of s.nodes.slice(0, maxNodes)) {
    lines.push(`\n### ${n.name}   [${n.type}]  id=${n.id}`);
    if (n.signature) lines.push(`  calls: ${n.signature}`);
    if (n.engine) lines.push(`  engine: ${n.engine}`);
    if (n.value !== undefined) lines.push(`  value: ${JSON.stringify(n.value)}`);
    for (const i of n.inputs) {
      lines.push(`  in  ${i.port || "(unnamed)"} <- ${i.from ?? "(nothing)"}`);
    }
    for (const o of n.outputs) {
      lines.push(`  out ${o.port || "(unnamed)"} -> ${o.to.length ? o.to.join(", ") : "(nothing)"}`);
    }
    if (n.code) {
      lines.push("  code:");
      lines.push(
        n.code
          .split("\n")
          .map((l) => "    " + l)
          .join("\n")
      );
    }
  }
  if (s.nodes.length > maxNodes) {
    lines.push(`\n… ${s.nodes.length - maxNodes} more nodes not shown (raise max_nodes to see them).`);
  }

  return lines.join("\n");
}
