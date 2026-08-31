/**
 * Harness for the memory and localisation layers.
 *
 * Run:  node build/memory/selfTest.js
 *
 * Every check that asserts a refusal is paired with a check that the same code path
 * ACCEPTS valid input, so a guard cannot pass by refusing everything. Nothing here
 * touches Revit or the network.
 */
import fs from "fs";
import os from "os";
import path from "path";

let passed = 0;
const failures: string[] = [];

function check(name: string, fn: () => void): void {
  try {
    fn();
    passed++;
    console.log(`  PASS  ${name}`);
  } catch (e) {
    failures.push(name);
    console.log(`  FAIL  ${name}\n        ${e instanceof Error ? e.message : String(e)}`);
  }
}

function assert(cond: unknown, msg: string): asserts cond {
  if (!cond) throw new Error(msg);
}

// Every test runs against a throwaway store, never the user's.
const sandbox = fs.mkdtempSync(path.join(os.tmpdir(), "revit-mcp-mem-"));
process.env.REVIT_MCP_DATA_DIR = sandbox;

const { dataDir } = await import("./paths.js");
const knowledge = await import("./knowledge.js");
const { ingestFile } = await import("./ingest.js");
const i18n = await import("../i18n/index.js");

console.log(`memory + i18n harness    sandbox: ${sandbox}\n`);
console.log("== storage location ==");

check("REVIT_MCP_DATA_DIR is honoured", () => {
  assert(dataDir() === path.resolve(sandbox), `dataDir() = ${dataDir()}`);
});

check("the default store is NOT inside the package directory", () => {
  // The defect this replaces: a SQLite file resolved relative to __dirname, which
  // under `npx -y` lives in the npm cache and can be cleared without warning.
  delete process.env.REVIT_MCP_DATA_DIR;
  const def = dataDir();
  process.env.REVIT_MCP_DATA_DIR = sandbox;
  const here = path.resolve(new URL(".", import.meta.url).pathname.replace(/^\//, ""));
  assert(!def.toLowerCase().startsWith(here.toLowerCase()), `default ${def} is inside the package`);
  assert(!/_npx|npm-cache/i.test(def), `default ${def} is inside an npm cache`);
});

console.log("\n== knowledge memory ==");

check("add stores a unit and reports it", () => {
  const { unit, duplicate } = knowledge.add({
    ns: "revit.recipe",
    title: "Duct.Create needs a real system type",
    body: "Duct.Create(doc, systemTypeId, ductTypeId, levelId, start, end). InvalidElementId for the system type throws; resolve a MechanicalSystemType first.",
    tags: ["mep", "revit2026"],
  });
  assert(!duplicate, "first add reported as duplicate");
  assert(unit.id.length === 16, `id length ${unit.id.length}`);
});

check("identical content is reported as a duplicate, not stored twice", () => {
  const before = knowledge.stats().units;
  const { duplicate } = knowledge.add({
    ns: "revit.recipe",
    title: "Duct.Create needs a real system type",
    body: "Duct.Create(doc, systemTypeId, ductTypeId, levelId, start, end). InvalidElementId for the system type throws; resolve a MechanicalSystemType first.",
    tags: ["mep", "revit2026"],
  });
  assert(duplicate, "second identical add was not flagged");
  assert(knowledge.stats().units === before, "duplicate changed the unit count");
});

check("add REFUSES an empty namespace, title or body", () => {
  for (const bad of [
    { ns: "", title: "t", body: "b" },
    { ns: "x", title: "", body: "b" },
    { ns: "x", title: "t", body: "" },
  ]) {
    let threw = false;
    try {
      knowledge.add(bad as any);
    } catch {
      threw = true;
    }
    assert(threw, `accepted ${JSON.stringify(bad)}`);
  }
});

check("search finds a unit by a term in its body", () => {
  const hits = knowledge.search("MechanicalSystemType");
  assert(hits.length > 0, "no hits");
  assert(hits[0].matched.includes("mechanicalsystemtype"), `matched: ${hits[0].matched}`);
});

check("search returns nothing for a term that is absent (not everything)", () => {
  // The red path for the green above: a scorer that returns all units for any query
  // would pass the previous check and be useless.
  const hits = knowledge.search("zzzznotpresentanywhere");
  assert(hits.length === 0, `${hits.length} hits for an absent term`);
});

check("namespace filter actually restricts", () => {
  knowledge.add({ ns: "project.standard", title: "Levels are named L01..L09", body: "Two digits, zero padded, no space." });
  const all = knowledge.search("levels");
  const scoped = knowledge.search("levels", { ns: "revit.recipe" });
  assert(all.length > scoped.length, `all=${all.length} scoped=${scoped.length}`);
});

check("stats reports the namespaces present", () => {
  const s = knowledge.stats();
  assert(s.namespaces["revit.recipe"] >= 1, "revit.recipe missing");
  assert(s.namespaces["project.standard"] >= 1, "project.standard missing");
});

console.log("\n== ingestion ==");

check("markdown is split on headings", () => {
  const f = path.join(sandbox, "sample.md");
  fs.writeFileSync(f, "# One\nbody one, long enough to survive the minimum length filter.\n\n## Two\nbody two, also long enough to survive the minimum length filter.\n");
  const r = ingestFile(f, "doc.sample");
  assert(r.units === 2, `${r.units} units`);
  assert(r.added === 2, `${r.added} added`);
});

check("re-ingesting the same file adds nothing", () => {
  const f = path.join(sandbox, "sample.md");
  const r = ingestFile(f, "doc.sample");
  assert(r.added === 0, `${r.added} added on re-ingest`);
  assert(r.duplicates === 2, `${r.duplicates} duplicates`);
});

check("text with page markers is split per page", () => {
  const f = path.join(sandbox, "pages.txt");
  fs.writeFileSync(f, "Page 1\nAlpha chain, with enough words to clear the length filter.\n\nPage 2\nBeta chain, with enough words to clear the length filter.\n");
  const r = ingestFile(f, "doc.pages");
  assert(r.units === 2, `${r.units} units from 2 pages`);
});

check("PDF is REFUSED with conversion instructions, not half-parsed", () => {
  const f = path.join(sandbox, "thing.pdf");
  fs.writeFileSync(f, "%PDF-1.7 not really a pdf");
  let msg = "";
  try {
    ingestFile(f, "doc.pdf");
  } catch (e) {
    msg = e instanceof Error ? e.message : String(e);
  }
  assert(msg.includes("pdftotext"), `refusal did not name a conversion route: ${msg}`);
});

check("a supported extension is still accepted (the refusal is not blanket)", () => {
  const f = path.join(sandbox, "ok.txt");
  fs.writeFileSync(f, "A single block of text that is comfortably longer than the minimum.\n");
  const r = ingestFile(f, "doc.ok");
  assert(r.added === 1, `${r.added} added`);
});

console.log("\n== optional localisation ==");

check("English is the default and t() is an identity function", () => {
  delete process.env.REVIT_MCP_LOCALE;
  i18n._reset();
  assert(i18n.activeLocale() === "en", i18n.activeLocale());
  assert(i18n.t("Create a wall") === "Create a wall", "t() altered text with no locale set");
  assert(i18n.localeStatus().isDefault, "status does not report the default");
});

check("an unknown locale falls back to English instead of failing", () => {
  process.env.REVIT_MCP_LOCALE = "xx-Fake";
  i18n._reset();
  assert(i18n.t("Create a wall") === "Create a wall", "unknown locale changed the text");
  delete process.env.REVIT_MCP_LOCALE;
  i18n._reset();
});

check("the shipped zh-Hans catalogue loads and translates", () => {
  process.env.REVIT_MCP_LOCALE = "zh-Hans";
  i18n._reset();
  const status = i18n.localeStatus();
  assert(status.entries > 0, "catalogue is empty");
  // Find any key and confirm it round-trips to something different.
  const file = new URL("../i18n/locales/zh-Hans.json", import.meta.url);
  const cat = JSON.parse(fs.readFileSync(file, "utf8"));
  const key = Object.keys(cat.strings)[0];
  assert(i18n.t(key) === cat.strings[key], `t(${JSON.stringify(key)}) did not return the catalogue value`);
  assert(i18n.t(key) !== key, "translation equals the English key");
});

check("an untranslated string still comes back in English under a locale", () => {
  const untranslated = "a string that is certainly not in the catalogue " + Date.now();
  assert(i18n.t(untranslated) === untranslated, "untranslated string was altered");
  delete process.env.REVIT_MCP_LOCALE;
  i18n._reset();
});

check("the catalogue is tagged zh-Hans, not 'Mandarin'", () => {
  const file = new URL("../i18n/locales/zh-Hans.json", import.meta.url);
  const cat = JSON.parse(fs.readFileSync(file, "utf8"));
  assert(cat.locale === "zh-Hans", `locale tag is ${cat.locale}`);
  assert(/Simplified/i.test(cat.script || ""), `script is ${cat.script}`);
  assert(/Mandarin/i.test(cat.note || ""), "the note does not explain the Mandarin distinction");
});

console.log(`\n${passed} passed, ${failures.length} failed`);
if (failures.length) {
  console.log("FAILED: " + failures.join("; "));
  process.exit(1);
}
console.log("ALL PASS");
