/**
 * One place that turns a Revit reply into an MCP tool result.
 *
 * WHY. Every tool used to do `JSON.stringify(response, null, 2)` and hand the
 * text back regardless of size or outcome. Measured on a real model, the
 * 2-space indentation alone added ~45% to every payload, a single
 * get_current_view_elements call was 34 KB, and a failure came back as ordinary
 * text with no `isError`, so a small model read "Create wall failed: ..." as a
 * success and carried on. Everything below exists to close those three holes:
 *
 *   - compact JSON, nulls / empty strings / noise keys pruned, floats rounded;
 *   - a hard size cap, applied to the LARGEST ARRAY first so the reader still
 *     gets whole records plus an explicit `_truncated` marker rather than a
 *     mid-string cut;
 *   - failures always carry `isError: true` and an `{ok:false, error}` body.
 *
 * REVIT_MCP_MAX_RESULT_CHARS overrides the cap; REVIT_MCP_KEEP_UNIQUE_IDS=1
 * keeps the 45-character UniqueId strings that no other tool consumes.
 */
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { withRevitConnection } from "./ConnectionManager.js";

function envInt(name: string, fallback: number): number {
  const v = Number(process.env[name]);
  return Number.isFinite(v) && v > 0 ? v : fallback;
}

export const MAX_RESULT_CHARS = envInt("REVIT_MCP_MAX_RESULT_CHARS", 20000);

const NOISE_KEYS: ReadonlySet<string> =
  process.env.REVIT_MCP_KEEP_UNIQUE_IDS === "1" ? new Set() : new Set(["UniqueId", "uniqueId"]);

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Drop what carries no information: null, undefined, empty strings, noise keys.
 * Round floats to 3 decimals (millimetre precision is 0.001 mm — more than any
 * Revit workflow needs, and internal-feet conversions otherwise emit 15 digits).
 * Integers, ids included, are untouched.
 */
export function prune(value: unknown, depth = 0): unknown {
  if (value === null || value === undefined) return undefined;
  if (depth > 12) return value;
  if (Array.isArray(value)) {
    const out: unknown[] = [];
    for (const item of value) {
      const p = prune(item, depth + 1);
      if (p !== undefined) out.push(p);
    }
    return out;
  }
  if (typeof value === "object") {
    const out: Record<string, unknown> = {};
    for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
      if (NOISE_KEYS.has(key)) continue;
      const p = prune(raw, depth + 1);
      if (p === undefined) continue;
      if (typeof p === "string" && p.length === 0) continue;
      out[key] = p;
    }
    return out;
  }
  if (typeof value === "number") {
    if (!Number.isFinite(value)) return String(value);
    return Number.isInteger(value) ? value : Math.round(value * 1000) / 1000;
  }
  return value;
}

type ArrayRef = { parent: Record<string, unknown> | unknown[]; key: string | number; arr: unknown[]; path: string; size: number };

/** The array that dominates the payload, if any — that is where truncation must happen. */
function largestArray(node: unknown, path = "", depth = 0): ArrayRef | null {
  if (depth > 4 || node === null || typeof node !== "object") return null;
  let best: ArrayRef | null = null;
  const consider = (candidate: ArrayRef | null) => {
    if (candidate && (!best || candidate.size > best.size)) best = candidate;
  };
  const entries: Array<[string | number, unknown]> = Array.isArray(node)
    ? node.map((v, i) => [i, v] as [number, unknown])
    : Object.entries(node as Record<string, unknown>);
  for (const [key, child] of entries) {
    const childPath = path ? `${path}.${key}` : String(key);
    if (Array.isArray(child) && child.length > 1) {
      consider({ parent: node as Record<string, unknown> | unknown[], key, arr: child, path: childPath, size: JSON.stringify(child).length });
    }
    consider(largestArray(child, childPath, depth + 1));
  }
  return best;
}

function setAt(ref: ArrayRef, arr: unknown[]): void {
  if (Array.isArray(ref.parent)) ref.parent[ref.key as number] = arr;
  else ref.parent[ref.key as string] = arr;
}

/**
 * Serialise `value` compactly, within `max` characters.
 *
 * When the payload is too large and one array dominates it, that array is cut to
 * the longest prefix that fits and `_truncated` says what was left out. Only when
 * no array can absorb the cut does the text itself get sliced.
 */
export function render(value: unknown, max: number = MAX_RESULT_CHARS): string {
  if (typeof value === "string") return value.length <= max ? value : value.slice(0, max) + `\n...[truncated ${value.length - max} of ${value.length} chars]`;
  const data = prune(value);
  let text = JSON.stringify(data);
  if (text === undefined) return "";
  if (text.length <= max) return text;

  const target = largestArray(data);
  if (target && data !== null && typeof data === "object") {
    const total = target.arr.length;
    let lo = 0;
    let hi = total - 1;
    while (lo < hi) {
      const mid = Math.ceil((lo + hi) / 2);
      setAt(target, target.arr.slice(0, mid));
      if (JSON.stringify(data).length <= max - 160) lo = mid;
      else hi = mid - 1;
    }
    setAt(target, target.arr.slice(0, lo));
    const marker = { field: target.path, shown: lo, total, hint: "More records exist. Narrow the query (category, filter, limit) to see the rest." };
    if (Array.isArray(data)) text = JSON.stringify({ items: data, _truncated: marker });
    else text = JSON.stringify({ ...(data as Record<string, unknown>), _truncated: marker });
    if (text.length <= max + 400) return text;
  }
  return text.slice(0, max) + `\n...[truncated ${text.length - max} of ${text.length} chars; narrow the query]`;
}

/** A successful result. Strings pass through; anything else is rendered compactly. */
export function ok(data: unknown): CallToolResult {
  return { content: [{ type: "text", text: render(data) }] };
}

/** A failed result: `isError` set, body explains what went wrong and, when known, what to do. */
export function fail(message: string, extra?: Record<string, unknown>): CallToolResult {
  return { content: [{ type: "text", text: render({ ok: false, error: message, ...(extra ?? {}) }) }], isError: true };
}

/**
 * Classify a Revit reply. The command set is not uniform: some handlers answer
 * `{success:false, message}`, some `{ok:false, message}`, some `{Success:false,
 * ErrorMessage}`. All of them mean the action did NOT happen, and all of them
 * must reach the model as an error.
 */
export function fromRevit(response: unknown, label?: string): CallToolResult {
  if (response !== null && typeof response === "object" && !Array.isArray(response)) {
    const r = response as Record<string, unknown>;
    const failed = r.success === false || r.ok === false || r.Success === false || r.Ok === false;
    if (failed) {
      const message = [r.message, r.errorMessage, r.error, r.Message, r.ErrorMessage, r.Warning, r.warning]
        .find((m) => typeof m === "string" && m.length > 0) as string | undefined;
      const { success: _s, ok: _o, Success: _S, Ok: _O, message: _m, errorMessage: _e, error: _err, Message: _M, ErrorMessage: _E, ...rest } = r;
      return fail(message ?? `${label ?? "Revit command"} failed`, rest);
    }
  }
  return ok(response);
}

/**
 * Send one command to Revit and turn the outcome into a tool result. Connection
 * failures already carry the "is Revit running / is the switch on" guidance from
 * ConnectionManager, so only the label is added here.
 */
export async function callRevit(command: string, params: unknown, label: string = command): Promise<CallToolResult> {
  try {
    const response = await withRevitConnection(async (client) => client.sendCommand(command, params));
    return fromRevit(response, label);
  } catch (error) {
    return fail(`${label} failed: ${errorMessage(error)}`);
  }
}

/** Wrap a tool body so an unexpected throw becomes an error result rather than a protocol error. */
export async function guarded(label: string, body: () => Promise<CallToolResult>): Promise<CallToolResult> {
  try {
    return await body();
  } catch (error) {
    return fail(`${label} failed: ${errorMessage(error)}`);
  }
}
