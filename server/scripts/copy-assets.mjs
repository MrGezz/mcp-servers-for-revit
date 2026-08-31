// tsc compiles .ts and copies NOTHING else, so any non-TypeScript asset under src/
// is absent from build/ unless it is copied here.
//
// This is not theoretical: the locale catalogue was written to src/i18n/locales/ and
// then never shipped, so requesting a locale logged "catalogue could not be read"
// and silently fell back to English. The harness caught it; this step fixes it.
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const here = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.join(here, "..", "src");
const OUT = path.join(here, "..", "build");

let copied = 0;
function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const from = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(from);
      continue;
    }
    if (!/\.(json|md)$/i.test(entry.name)) continue;
    const to = path.join(OUT, path.relative(SRC, from));
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.copyFileSync(from, to);
    copied++;
    console.log(`  copied ${path.relative(SRC, from).replace(/\\/g, "/")}`);
  }
}

if (!fs.existsSync(OUT)) {
  console.error("copy-assets: build/ does not exist - run tsc first.");
  process.exit(1);
}
walk(SRC);
console.log(`copy-assets: ${copied} asset(s) copied into build/`);

// A locale catalogue that is present in src but missing from build is the exact
// failure this script exists to prevent, so assert it rather than hope.
const locales = path.join(SRC, "i18n", "locales");
if (fs.existsSync(locales)) {
  for (const f of fs.readdirSync(locales)) {
    const shipped = path.join(OUT, "i18n", "locales", f);
    if (!fs.existsSync(shipped)) {
      console.error(`copy-assets: FAILED to ship ${f} - it exists in src but not in build.`);
      process.exit(1);
    }
  }
  console.log(`copy-assets: all ${fs.readdirSync(locales).length} locale catalogue(s) present in build/`);
}
