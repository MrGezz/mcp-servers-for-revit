import fs from "fs";
import path from "path";
import { addMany, KnowledgeSource } from "./knowledge.js";

/**
 * Bulk ingestion into knowledge memory.
 *
 * The point of this module is volume: a reference document is worth having in the
 * store as a hundred addressable units, not as one blob nobody can search.
 *
 * Deliberately NOT included: PDF, DOCX and PPTX parsing. Each needs a heavy
 * dependency (and PDF needs a native one), and this server is shipped inside an
 * installer where every added dependency is weight the user carries forever.
 * Those formats are REFUSED with instructions rather than half-supported, because
 * a parser that silently produces garbage from a two-column slide layout is worse
 * than one that declines.
 */

export type IngestFormat = "markdown" | "text" | "json" | "csv";

const TEXT_EXT: Record<string, IngestFormat> = {
  ".md": "markdown",
  ".markdown": "markdown",
  ".txt": "text",
  ".text": "text",
  ".json": "json",
  ".jsonl": "json",
  ".csv": "csv",
  ".tsv": "csv",
};

const REFUSED: Record<string, string> = {
  ".pdf": "pdftotext -layout in.pdf out.txt   (poppler), or any PDF-to-text export",
  ".docx": "Save As > Plain Text, or pandoc -t markdown",
  ".doc": "Save As > Plain Text",
  ".pptx": "Export the notes/outline as text, or pandoc",
  ".ppt": "Export the notes/outline as text",
  ".xlsx": "Save As > CSV",
  ".xls": "Save As > CSV",
};

export interface IngestResult {
  file: string;
  format: IngestFormat;
  units: number;
  added: number;
  duplicates: number;
  ns: string;
  notes: string[];
}

function splitMarkdown(text: string): Array<{ title: string; body: string }> {
  const lines = text.split(/\r?\n/);
  const out: Array<{ title: string; body: string }> = [];
  let title: string | null = null;
  let buf: string[] = [];
  const flush = () => {
    const body = buf.join("\n").trim();
    if (title && body) out.push({ title, body });
    buf = [];
  };
  for (const line of lines) {
    const h = line.match(/^(#{1,4})\s+(.*\S)\s*$/);
    if (h) {
      flush();
      title = h[2];
      continue;
    }
    buf.push(line);
  }
  flush();
  return out;
}

function splitText(text: string): Array<{ title: string; body: string }> {
  // Honour an explicit page/section marker if the exporter produced one, since
  // that is a far better unit boundary than a blank line.
  const pageMarker = /^\s*(?:---\s*)?(?:page|slide|section)\s+(\d+)(?:\s+of\s+\d+)?\s*(?:---)?\s*$/i;
  const lines = text.split(/\r?\n/);
  const hasMarkers = lines.some((l) => pageMarker.test(l));

  const blocks: Array<{ title: string; body: string }> = [];
  if (hasMarkers) {
    let label = "";
    let buf: string[] = [];
    const flush = () => {
      const body = buf.join("\n").trim();
      if (body) {
        const first = body.split(/\n/).find((l) => l.trim()) || label;
        blocks.push({ title: `${label}: ${first.trim()}`.slice(0, 160), body });
      }
      buf = [];
    };
    for (const line of lines) {
      const m = line.match(pageMarker);
      if (m) {
        flush();
        label = `Page ${m[1]}`;
        continue;
      }
      buf.push(line);
    }
    flush();
    return blocks;
  }

  // Otherwise: blank-line separated blocks, first non-empty line as the title.
  let buf: string[] = [];
  const flush = () => {
    const body = buf.join("\n").trim();
    if (body.length > 40) {
      const first = body.split(/\n/)[0].trim();
      blocks.push({ title: first.slice(0, 160), body });
    }
    buf = [];
  };
  for (const line of lines) {
    if (!line.trim()) {
      flush();
      continue;
    }
    buf.push(line);
  }
  flush();
  return blocks;
}

function splitJson(text: string): Array<{ title: string; body: string; tags?: string[] }> {
  const trimmed = text.trim();
  const rows: any[] = [];
  if (trimmed.startsWith("[")) {
    rows.push(...JSON.parse(trimmed));
  } else {
    // JSON Lines, or a single object treated as a title -> body map.
    const lines = trimmed.split(/\r?\n/).filter((l) => l.trim());
    let allLines = true;
    for (const l of lines) {
      try {
        rows.push(JSON.parse(l));
      } catch {
        allLines = false;
        break;
      }
    }
    if (!allLines) {
      rows.length = 0;
      const obj = JSON.parse(trimmed);
      for (const [k, v] of Object.entries(obj)) {
        rows.push({ title: k, body: typeof v === "string" ? v : JSON.stringify(v, null, 1) });
      }
    }
  }
  return rows
    .map((r) => ({
      title: String(r.title ?? r.name ?? r.id ?? "").trim(),
      body: typeof r.body === "string" ? r.body : JSON.stringify(r, null, 1),
      tags: Array.isArray(r.tags) ? r.tags.map(String) : undefined,
    }))
    .filter((r) => r.title && r.body);
}

function splitCsv(text: string, sep: string): Array<{ title: string; body: string }> {
  const lines = text.split(/\r?\n/).filter((l) => l.trim());
  if (!lines.length) return [];
  const header = lines[0].split(sep).map((h) => h.trim());
  const out: Array<{ title: string; body: string }> = [];
  for (const line of lines.slice(1)) {
    const cells = line.split(sep);
    if (!cells.length || !cells[0].trim()) continue;
    const body = header
      .map((h, i) => `${h}: ${(cells[i] ?? "").trim()}`)
      .filter((s) => !/:\s*$/.test(s))
      .join("\n");
    out.push({ title: cells[0].trim().slice(0, 160), body });
  }
  return out;
}

export function ingestFile(
  file: string,
  ns: string,
  opts: { tags?: string[]; minLength?: number } = {}
): IngestResult {
  const abs = path.resolve(file);
  const ext = path.extname(abs).toLowerCase();

  if (REFUSED[ext]) {
    throw new Error(
      `Cannot ingest ${ext} directly - this server does not carry a ${ext.slice(1).toUpperCase()} ` +
        `parser, because it would add a heavy (and for PDF, native) dependency to a package that ` +
        `ships inside an installer, and a bad parse of a multi-column layout produces convincing ` +
        `nonsense. Convert it to text first, then ingest the text:\n    ${REFUSED[ext]}`
    );
  }

  const format = TEXT_EXT[ext];
  if (!format) {
    throw new Error(
      `Cannot ingest ${ext || "(no extension)"} - supported: ` +
        `${[...new Set(Object.values(TEXT_EXT))].join(", ")} ` +
        `(${Object.keys(TEXT_EXT).join(" ")})`
    );
  }
  if (!fs.existsSync(abs)) throw new Error(`No such file: ${abs}`);

  const text = fs.readFileSync(abs, "utf8");
  const notes: string[] = [];
  let blocks: Array<{ title: string; body: string; tags?: string[] }>;
  switch (format) {
    case "markdown":
      blocks = splitMarkdown(text);
      if (!blocks.length) {
        notes.push("no markdown headings found; fell back to blank-line blocks");
        blocks = splitText(text);
      }
      break;
    case "json":
      blocks = splitJson(text);
      break;
    case "csv":
      blocks = splitCsv(text, ext === ".tsv" ? "\t" : ",");
      break;
    default:
      blocks = splitText(text);
  }

  const min = opts.minLength ?? 40;
  const kept = blocks.filter((b) => b.body.trim().length >= min);
  if (kept.length !== blocks.length) {
    notes.push(`${blocks.length - kept.length} block(s) shorter than ${min} chars were skipped`);
  }

  const source: KnowledgeSource = { kind: "document", ref: abs };
  const { added, duplicates } = addMany(
    kept.map((b) => ({
      ns,
      title: b.title,
      body: b.body,
      tags: [...(opts.tags || []), ...(b.tags || [])],
      source,
    }))
  );

  return { file: abs, format, units: kept.length, added, duplicates, ns, notes };
}
