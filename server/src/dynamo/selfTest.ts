/**
 * Harness for the Dynamo graph reader/writer.
 *
 *   node build/dynamo/selfTest.js [directory-of-real-graphs]
 *
 * Two kinds of check, and the distinction matters:
 *
 *   SYNTHETIC   a graph built here, so every field is known. Proves the mutation
 *               and validation logic.
 *
 *   CORPUS      real `.dyn` files written by Dynamo itself, if a directory is
 *               given. Proves the one property no synthetic fixture can:
 *               read -> write with no edits is byte-identical. A fixture I wrote
 *               only contains fields I already thought of, which is exactly the
 *               blind spot round-trip fidelity is about.
 *
 * Several checks are RED-PATH checks: they assert that known-bad input is
 * REFUSED. A validator that has never been seen to fail is not evidence of
 * anything.
 */

import {
  addFunctionNode,
  newGraph,
  DynDocument,
  LoadedGraph,
  readGraph,
  serializeGraph,
  listGraphFiles,
  validate,
  connect,
  disconnect,
  setCode,
  setInputValue,
  removeNode,
  addCodeBlock,
  addPythonNode,
  findNode,
  resolvePort,
  moveNode,
  newId,
  NUMBER_FIDELITY_AVAILABLE,
} from "./DynGraph.js";
import { summarize, renderSummary } from "./summarize.js";

let passed = 0;
let failed = 0;

function check(name: string, condition: boolean, detail = ""): void {
  if (condition) {
    passed++;
    console.log(`PASS  ${name}`);
  } else {
    failed++;
    console.log(`FAIL  ${name}${detail ? ` — ${detail}` : ""}`);
  }
}

/** Assert that `fn` throws, and that the message mentions `expect`. */
function refuses(name: string, fn: () => unknown, expect: string): void {
  try {
    fn();
    check(name, false, "expected a refusal, got success");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    check(name, message.toLowerCase().includes(expect.toLowerCase()), `message was: ${message}`);
  }
}

function section(title: string): void {
  console.log(`\n--- ${title}`);
}

// ---------------------------------------------------------------------------
// A synthetic graph shaped like a real one.
// ---------------------------------------------------------------------------

function fixture(): LoadedGraph {
  const aId = newId();
  const bId = newId();
  const aOut = newId();
  const bIn = newId();
  const bOut = newId();

  const doc: DynDocument = {
    Uuid: newId(),
    IsCustomNode: false,
    Description: "fixture",
    Name: "Fixture",
    Nodes: [
      {
        ConcreteType: "CoreNodeModels.Input.DoubleInput, CoreNodeModels",
        Id: aId,
        NodeType: "NumberInputNode",
        NumberType: "Double",
        InputValue: 42,
        Inputs: [],
        Outputs: [{ Id: aOut, Name: "" }],
      },
      {
        ConcreteType: "PythonNodeModels.PythonNode, PythonNodeModels",
        Id: bId,
        NodeType: "PythonScriptNode",
        Engine: "CPython3",
        Code: "OUT = IN[0]",
        Inputs: [{ Id: bIn, Name: "IN[0]" }],
        Outputs: [{ Id: bOut, Name: "OUT" }],
      },
    ],
    Connectors: [],
    Inputs: [{ Id: aId, Name: "Count", Type: "number", Value: "42" }],
    // A key this module has never heard of. It must survive every rewrite.
    SomeFutureDynamoKey: { nested: [1, 2, 3] },
    View: {
      Dynamo: { Version: "4.1.1.5050", RunType: "Manual" },
      NodeViews: [
        { Id: aId, Name: "Number", X: 0, Y: 0 },
        { Id: bId, Name: "Python Script", X: 300, Y: 0 },
      ],
      Annotations: [],
    },
  };

  return {
    filePath: "(fixture).dyn",
    doc,
    originalBytes: 0,
    trailingNewline: false,
    eol: "LF",
    numberFidelity: NUMBER_FIDELITY_AVAILABLE,
  };
}

// ---------------------------------------------------------------------------

async function main(): Promise<void> {
  section("new graph");
  {
    const g = newGraph("C:/anywhere/Fresh.dyn");
    check("newGraph names the graph after the file", g.doc.Name === "Fresh");
    check("newGraph has an empty Nodes array", Array.isArray(g.doc.Nodes) && g.doc.Nodes.length === 0);
    check("newGraph validates clean", validate(g.doc).filter((p) => p.severity === "error").length === 0);
    addPythonNode(g.doc, "OUT = 1");
    check("newGraph accepts a node", g.doc.Nodes!.length === 1 && g.doc.View!.NodeViews!.length === 1);
    check("...and still validates clean", validate(g.doc).filter((p) => p.severity === "error").length === 0);

    const fnode = addFunctionNode(g.doc, "DSOffice.Data.OpenXMLExportExcel@string,string,var[][],int,int,bool,bool", {
      inputs: ["filePath", "sheetName", "data", "startRow", "startColumn", "overWrite", "writeAsString"],
      outputs: ["bool"],
      defaults: ["startRow", "startColumn", "writeAsString"],
      position: { x: 300, y: 0 },
    });
    check("addFunctionNode builds a DSFunction node", fnode.NodeType === "FunctionNode" && fnode.Inputs!.length === 7);
    check("...marking defaulted inputs", fnode.Inputs![3].UsingDefaultValue === true && fnode.Inputs![2].UsingDefaultValue === false);
    check("...named after the class and method on the canvas", g.doc.View!.NodeViews!.some((v) => v.Name === "Data.OpenXMLExportExcel"));
    connect(g.doc, { node: "Python Script", port: "OUT" }, { node: "Data.OpenXMLExportExcel", port: "data" });
    check("...and can be wired by port name", validate(g.doc).filter((p) => p.severity === "error").length === 0);
    refuses(
      "addFunctionNode refuses an input count that does not match the signature",
      () => addFunctionNode(g.doc, "DSCore.List.Transpose@var[]..[]", { inputs: ["a", "b"] }),
      "1 argument"
    );
  }

  section("lookup");
  {
    const g = fixture();
    const byName = findNode(g.doc, "Python Script");
    check("findNode resolves by canvas name", byName.NodeType === "PythonScriptNode");
    check("findNode resolves by id", findNode(g.doc, byName.Id).Id === byName.Id);
    check("findNode resolves by substring", findNode(g.doc, "Numb").NodeType === "NumberInputNode");
    refuses("findNode refuses an unknown selector", () => findNode(g.doc, "nope"), "no node");

    const py = findNode(g.doc, "Python Script");
    check("resolvePort by index", resolvePort(py, "in", 0) === py.Inputs![0].Id);
    check("resolvePort by name", resolvePort(py, "out", "OUT") === py.Outputs![0].Id);
    refuses("resolvePort refuses an out-of-range index", () => resolvePort(py, "in", 9), "out of range");
    refuses("resolvePort refuses an unknown port name", () => resolvePort(py, "in", "nope"), "no in port");
  }

  section("wiring");
  {
    const g = fixture();
    const r = connect(g.doc, { node: "Number" }, { node: "Python Script" });
    check("connect adds a connector", g.doc.Connectors!.length === 1);
    check("connect reports no replacement on a free input", r.replaced === null);
    check("connect wires port ids, not node ids", g.doc.Connectors![0].Start === findNode(g.doc, "Number").Outputs![0].Id);
    check("a wired graph validates clean", validate(g.doc).filter((p) => p.severity === "error").length === 0);

    // Dynamo permits exactly one source per input; a second must displace the first.
    const again = connect(g.doc, { node: "Number" }, { node: "Python Script" });
    check("second connect to the same input replaces rather than duplicates", g.doc.Connectors!.length === 1);
    check("...and reports which connector it displaced", again.replaced === r.connectorId);

    check("disconnect by node removes it", disconnect(g.doc, { node: "Python Script" }) === 1);
    check("...leaving no connectors", g.doc.Connectors!.length === 0);
  }

  section("validation red paths");
  {
    const g = fixture();
    connect(g.doc, { node: "Number" }, { node: "Python Script" });

    // Hand-forge each corrupt state the validator claims to catch.
    const dangling = fixture();
    dangling.doc.Connectors = [{ Start: "deadbeef", End: "cafebabe", Id: newId() }];
    const problems = validate(dangling.doc);
    check(
      "validate catches a dangling connector",
      problems.filter((p) => p.kind === "dangling-connector").length === 2,
      JSON.stringify(problems)
    );

    const doubled = fixture();
    const target = findNode(doubled.doc, "Python Script").Inputs![0].Id;
    const source = findNode(doubled.doc, "Number").Outputs![0].Id;
    doubled.doc.Connectors = [
      { Start: source, End: target, Id: newId() },
      { Start: source, End: target, Id: newId() },
    ];
    check(
      "validate catches an input with two sources",
      validate(doubled.doc).some((p) => p.kind === "multiply-connected-input")
    );

    const noView = fixture();
    noView.doc.View!.NodeViews = [];
    check(
      "validate warns about a node with no canvas position",
      validate(noView.doc).filter((p) => p.kind === "missing-nodeview").length === 2
    );

    const orphanView = fixture();
    orphanView.doc.View!.NodeViews!.push({ Id: newId(), Name: "ghost", X: 0, Y: 0 });
    check("validate warns about an orphan NodeView", validate(orphanView.doc).some((p) => p.kind === "orphan-nodeview"));

    const dupPort = fixture();
    dupPort.doc.Nodes![1].Inputs![0].Id = dupPort.doc.Nodes![0].Outputs![0].Id;
    check("validate catches a duplicate port id", validate(dupPort.doc).some((p) => p.kind === "duplicate-port-id"));

    check("a clean graph produces no errors", validate(g.doc).filter((p) => p.severity === "error").length === 0);
  }

  section("mutation");
  {
    const g = fixture();
    setCode(g.doc, "Python Script", "OUT = IN[0] * 2");
    check("setCode replaces the body", findNode(g.doc, "Python Script").Code === "OUT = IN[0] * 2");
    refuses("setCode refuses a node with no Code field", () => setCode(g.doc, "Number", "x"), "no Code field");

    const v = setInputValue(g.doc, "Number", 99);
    check("setInputValue updates the node", findNode(g.doc, "Number").InputValue === 99);
    check("setInputValue also updates the Dynamo Player entry", v.playerEntryUpdated === true);
    check("...to the same value", g.doc.Inputs![0].Value === "99");
    refuses("setInputValue refuses a node with no InputValue", () => setInputValue(g.doc, "Python Script", 1), "no InputValue");

    moveNode(g.doc, "Number", 10, 20);
    check("moveNode writes to the NodeView, not the node", g.doc.View!.NodeViews![0].X === 10);

    const cb = addCodeBlock(g.doc, '"hello";', { x: 5, y: 6 });
    check("addCodeBlock adds a node", g.doc.Nodes!.length === 3);
    check("...and a matching NodeView", g.doc.View!.NodeViews!.some((nv) => nv.Id === cb.Id));
    check("...leaving the graph valid", validate(g.doc).filter((p) => p.severity === "error").length === 0);

    const py = addPythonNode(g.doc, "OUT = 1", { inputCount: 3, engine: "PythonNet3" });
    check("addPythonNode honours inputCount", (py.Inputs ?? []).length === 3);
    check("addPythonNode honours engine", py.Engine === "PythonNet3");
    check("addPythonNode ports are unique", validate(g.doc).filter((p) => p.kind === "duplicate-port-id").length === 0);
  }

  section("removal cleans up after itself");
  {
    const g = fixture();
    connect(g.doc, { node: "Number" }, { node: "Python Script" });
    const before = g.doc.View!.NodeViews!.length;
    const r = removeNode(g.doc, "Number");

    check("removeNode removes the node", g.doc.Nodes!.length === 1);
    check("removeNode removes its connectors", r.connectorsRemoved === 1 && g.doc.Connectors!.length === 0);
    check("removeNode removes its NodeView", g.doc.View!.NodeViews!.length === before - 1);
    check("removeNode removes its Dynamo Player entry", (g.doc.Inputs ?? []).length === 0);
    check(
      "...so nothing dangles",
      validate(g.doc).filter((p) => p.severity === "error").length === 0,
      JSON.stringify(validate(g.doc))
    );
  }

  section("round-trip fidelity (synthetic)");
  {
    const g = fixture();
    const once = serializeGraph(g);
    const twice = serializeGraph({ ...g, doc: JSON.parse(once) as DynDocument });
    check("serialize is idempotent", once === twice);
    check("an unknown top-level key survives", (JSON.parse(once) as DynDocument).SomeFutureDynamoKey !== undefined);
    check("the View block survives", (JSON.parse(once) as DynDocument).View?.NodeViews?.length === 2);
  }

  section("summary");
  {
    const g = fixture();
    connect(g.doc, { node: "Number" }, { node: "Python Script" });
    const s = summarize(g.doc, "(fixture).dyn");
    check("summary counts nodes", s.counts.nodes === 2);
    check("summary resolves the feeder by name", s.nodes[1].inputs[0].from === "Number");
    check("summary resolves the consumer by name", s.nodes[0].outputs[0].to[0] === "Python Script");
    check("summary carries the code body", s.nodes[1].code === "OUT = IN[0]");
    check("summary lists Player inputs", s.playerInputs.length === 1);
    const text = renderSummary(s);
    check("rendered summary mentions both nodes", text.includes("Number") && text.includes("Python Script"));

    const lonely = fixture();
    check("summary reports unwired nodes", summarize(lonely.doc, "x").orphans.length === 2);
  }

  // -------------------------------------------------------------------------
  // The corpus pass. This is the check that matters most, because it runs
  // against files this code did not write.
  // -------------------------------------------------------------------------
  const corpusDir = process.argv[2];
  if (!corpusDir) {
    section("corpus");
    console.log("SKIP  no directory given — pass one to prove round-trip fidelity against real graphs");
  } else {
    section(`corpus: ${corpusDir}`);
    const files = await listGraphFiles(corpusDir, true, 400);
    console.log(`      ${files.length} graph file(s) found`);

    let identical = 0;
    let differing: string[] = [];
    let unreadable: string[] = [];
    let legacyXml = 0;
    let totalNodes = 0;

    const { readFile } = await import("node:fs/promises");

    for (const f of files) {
      // Dynamo 1.x wrote .dyn/.dyf as XML. Refusing those is correct, not a
      // failure — but it must be told apart from a genuine parse defect, so the
      // format is detected from the bytes rather than inferred from the error.
      const head = (await readFile(f.path, "utf8")).replace(/^\uFEFF/, "").slice(0, 200).trimStart();
      if (head.startsWith("<")) {
        legacyXml++;
        try {
          await readGraph(f.path);
          unreadable.push(`${f.name}: XML graph was accepted, but it should have been refused`);
        } catch (error) {
          const message = error instanceof Error ? error.message : String(error);
          if (!/Dynamo 1\.x/.test(message)) {
            unreadable.push(`${f.name}: refused, but the message does not explain why: ${message}`);
          }
        }
        continue;
      }

      let loaded;
      try {
        loaded = await readGraph(f.path);
      } catch (error) {
        unreadable.push(`${f.name}: ${error instanceof Error ? error.message : String(error)}`);
        continue;
      }

      totalNodes += Array.isArray(loaded.doc.Nodes) ? loaded.doc.Nodes.length : 0;

      // No edits: what comes out must be what went in.
      const rewritten = serializeGraph(loaded);
      const originalRaw = await readFile(f.path, "utf8");
      const original = originalRaw.charCodeAt(0) === 0xfeff ? originalRaw.slice(1) : originalRaw;

      if (rewritten === original) identical++;
      else differing.push(`${f.name} (${original.length} -> ${rewritten.length} chars)`);

      // Summarising must never throw on a real graph, whatever it contains.
      summarize(loaded.doc, f.path);
    }

    const json = files.length - legacyXml;

    check(
      "every JSON corpus file parsed, and every legacy XML one was refused with a reason",
      unreadable.length === 0,
      unreadable.slice(0, 3).join(" | ")
    );
    check(
      `read -> write is byte-identical for all ${json} Dynamo 2.x corpus graphs`,
      differing.length === 0 && json > 0,
      differing.length ? `${differing.length} differ, e.g. ${differing.slice(0, 3).join(" | ")}` : "no files scanned"
    );
    check("this runtime preserves numeric literals", NUMBER_FIDELITY_AVAILABLE,
      "Node < 22: whole-number doubles will lose their trailing .0 (harmless, but noisy in a diff)");
    console.log(
      `      ${identical}/${json} identical · ${legacyXml} legacy XML refused · ` +
        `${totalNodes} nodes summarised without error`
    );
  }

  console.log(`\n${passed} passed, ${failed} failed`);
  if (failed > 0) process.exit(1);
  console.log("ALL PASS");
}

main().catch((error) => {
  console.error("harness crashed:", error);
  process.exit(1);
});
