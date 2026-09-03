/**
 * Harness for the reply helpers and the tool catalogue.
 *
 *   node build/utils/selfTest.js
 *
 * These are the two pieces every tool passes through, so a regression here is a
 * regression in all ~100 tools at once. Red-path checks are included on
 * purpose: a failure must come back as `isError`, an oversized array must be
 * cut with a marker, and a group name typo must not be silently accepted.
 */
import { fromRevit, prune, render, fail, ok, MAX_RESULT_CHARS } from "./reply.js";
import { GROUPS, groupOf, startupGroups } from "../catalog.js";

let passed = 0;
const failures: string[] = [];

function check(name: string, fn: () => void) {
  try {
    fn();
    passed++;
    console.log(`  PASS  ${name}`);
  } catch (e) {
    failures.push(name);
    console.log(`  FAIL  ${name}\n        ${e instanceof Error ? e.message : String(e)}`);
  }
}
function assert(cond: unknown, msg: string) {
  if (!cond) throw new Error(msg);
}

check("prune drops null, undefined, empty strings and UniqueId", () => {
  const out = prune({ a: null, b: undefined, c: "", d: 0, e: false, UniqueId: "x", f: { g: null, h: "k" }, i: [null, 1] }) as any;
  assert(!("a" in out) && !("b" in out) && !("c" in out), "null/undefined/empty kept");
  assert(out.d === 0 && out.e === false, "falsy values that carry information were dropped");
  assert(!("UniqueId" in out), "UniqueId kept");
  assert(!("g" in out.f) && out.f.h === "k", "nested prune wrong");
  assert(JSON.stringify(out.i) === "[1]", "array nulls kept");
});

check("prune rounds floats to 3 decimals and leaves integers alone", () => {
  const out = prune({ x: 1234.56789012, id: 14402995, n: 2.0 }) as any;
  assert(out.x === 1234.568, `rounding wrong: ${out.x}`);
  assert(out.id === 14402995 && out.n === 2, "integers changed");
});

check("render is compact JSON (no indentation)", () => {
  const text = render({ a: 1, b: [1, 2] });
  assert(text === '{"a":1,"b":[1,2]}', `unexpected: ${text}`);
});

check("render cuts the dominant array and adds _truncated", () => {
  const elements = Array.from({ length: 2000 }, (_, i) => ({ Id: 1000 + i, Name: `Element ${i}`, Category: "Walls" }));
  const text = render({ ViewName: "L1", TotalElementsInView: 2000, Elements: elements }, 5000);
  assert(text.length <= 5400, `too long: ${text.length}`);
  const parsed = JSON.parse(text);
  assert(parsed.ViewName === "L1", "scalar fields lost");
  assert(parsed._truncated && parsed._truncated.field === "Elements", "no _truncated marker on Elements");
  assert(parsed._truncated.total === 2000 && parsed._truncated.shown > 0 && parsed._truncated.shown < 2000, "marker counts wrong");
  assert(parsed.Elements.length === parsed._truncated.shown, "shown does not match array length");
  assert(parsed.Elements[0].Id === 1000, "records reordered");
});

check("render falls back to a text cut when nothing can be trimmed", () => {
  const text = render({ blob: "x".repeat(50000) }, 1000);
  assert(text.length < 1200 && text.includes("truncated"), "no text truncation");
});

check("render respects MAX_RESULT_CHARS by default", () => {
  const big = { rows: Array.from({ length: 5000 }, (_, i) => ({ i, s: "abcdefghij" })) };
  const text = render(big);
  assert(text.length <= MAX_RESULT_CHARS + 400, `over cap: ${text.length}`);
});

check("fail sets isError and ok:false", () => {
  const r = fail("boom", { hint: "h" });
  assert(r.isError === true, "isError missing");
  const body = JSON.parse((r.content[0] as any).text);
  assert(body.ok === false && body.error === "boom" && body.hint === "h", "body wrong");
});

check("ok never sets isError", () => {
  assert(!ok({ a: 1 }).isError && !ok("text").isError, "isError set on ok");
});

check("fromRevit classifies success:false / ok:false / Success:false as errors", () => {
  for (const r of [{ success: false, message: "m1" }, { ok: false, message: "m2" }, { Success: false, ErrorMessage: "m3" }]) {
    const out = fromRevit(r, "x");
    assert(out.isError === true, `not error: ${JSON.stringify(r)}`);
    const body = JSON.parse((out.content[0] as any).text);
    assert(/m[123]/.test(body.error), `message lost: ${body.error}`);
  }
  assert(!fromRevit({ success: true, count: 1 }).isError, "success:true flagged");
  assert(!fromRevit([1, 2, 3]).isError, "array flagged");
});

check("every tool name in GROUPS is unique and groupOf resolves it", () => {
  const seen = new Set<string>();
  for (const [group, def] of Object.entries(GROUPS)) {
    for (const name of def.tools) {
      assert(!seen.has(name), `duplicate tool ${name}`);
      seen.add(name);
      assert(groupOf(name) === group, `groupOf(${name}) = ${groupOf(name)}`);
    }
  }
  assert(groupOf("no_such_tool") === "other", "unknown name not 'other'");
  assert(seen.has("revit_tools") && GROUPS.core.tools.includes("revit_tools"), "revit_tools must be core");
});

check("startupGroups honours REVIT_MCP_PROFILE and REVIT_MCP_TOOLS", () => {
  const prevP = process.env.REVIT_MCP_PROFILE;
  const prevT = process.env.REVIT_MCP_TOOLS;
  try {
    process.env.REVIT_MCP_PROFILE = "core";
    process.env.REVIT_MCP_TOOLS = "";
    assert([...startupGroups()].join() === "core", "core profile wrong");
    process.env.REVIT_MCP_PROFILE = "full";
    assert(startupGroups().size === Object.keys(GROUPS).length, "full profile wrong");
    process.env.REVIT_MCP_PROFILE = "core";
    process.env.REVIT_MCP_TOOLS = "views,+mep,-core";
    const g = startupGroups();
    assert(g.has("views") && g.has("mep") && g.has("core"), "REVIT_MCP_TOOLS not applied / core removable");
    process.env.REVIT_MCP_PROFILE = "bogus";
    process.env.REVIT_MCP_TOOLS = "";
    assert([...startupGroups()].join() === "core", "unknown profile must fall back to core");
  } finally {
    if (prevP === undefined) delete process.env.REVIT_MCP_PROFILE; else process.env.REVIT_MCP_PROFILE = prevP;
    if (prevT === undefined) delete process.env.REVIT_MCP_TOOLS; else process.env.REVIT_MCP_TOOLS = prevT;
  }
});

console.log(`\n${passed} passed, ${failures.length} failed`);
if (failures.length) {
  console.log("FAILED: " + failures.join("; "));
  process.exit(1);
}
console.log("ALL PASS");
