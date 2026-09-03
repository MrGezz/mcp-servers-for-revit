/**
 * Live Dynamo control: driving the Dynamo instance inside a running Revit.
 *
 * ---------------------------------------------------------------------------
 * READING A GRAPH NEEDS NOTHING. RUNNING ONE NEEDS A LIVE SESSION.
 * ---------------------------------------------------------------------------
 * Reading and writing a `.dyn` needs no Revit at all — that is `DynGraph.ts`,
 * and it is the part of this feature that always works. Opening a graph and
 * RUNNING it needs a live session, which this module reaches in one of two ways.
 *
 *   "native"  this project's own transport: a JSON-RPC `dynamo_op` command over
 *             the plugin socket, handled by the command set. This is the
 *             default and needs nothing installed beyond the add-in itself.
 *
 *   "http"    an OPTIONAL escape hatch for sites that already run their own
 *             bridge into Revit. Off unless `REVIT_MCP_DYNAMO_HTTP_URL` is set;
 *             this project ships no such endpoint and does not assume one.
 *
 * ---------------------------------------------------------------------------
 * COLD START
 * ---------------------------------------------------------------------------
 * Dynamo's assemblies are in the Revit process from start-up, but its MODEL
 * only exists once Dynamo has been opened. Until v1.0.1 that meant a human had
 * to click Manage > Dynamo before any graph could be opened or run. The command
 * set now has a `launch` op that posts Revit's own ID_VISUAL_PROGRAMMING_DYNAMO
 * command — the exact equivalent of that click — and `ensureDynamoReady` below
 * polls `status` until the model answers (measured: ~14 s on Revit 2026 /
 * Dynamo 3.6). No user interaction is needed.
 *
 * ---------------------------------------------------------------------------
 * THE HTTP CONTRACT, IF YOU IMPLEMENT ONE
 * ---------------------------------------------------------------------------
 *   POST <REVIT_MCP_DYNAMO_HTTP_URL>   { "op": "status" | "launch" | "open" | "run", ... }
 *   200 { "ok": true,  ... }  |  { "ok": false, "message": "..." }
 *
 * ---------------------------------------------------------------------------
 * WHY THERE IS NO DRY RUN
 * ---------------------------------------------------------------------------
 * A Dynamo graph opens and commits its own transactions, in its own order, so
 * there is nothing to roll back into. `run` takes an explicit confirm rather
 * than pretending a rollback exists.
 */

import { withRevitConnection } from "../utils/ConnectionManager.js";

export type BackendName = "native" | "http";

export interface BackendResult {
  backend: BackendName;
  ok: boolean;
  op: string;
  data: unknown;
}

/** Shape of the command set's `status` reply (fields are best-effort). */
export interface DynamoStatusData {
  ok?: boolean;
  loaded?: boolean;
  model_reachable?: boolean;
  current_workspace?: string | null;
  message?: string;
  [key: string]: unknown;
}

const HTTP_URL = process.env.REVIT_MCP_DYNAMO_HTTP_URL || "";
/** "auto" (default) | "native" | "http" */
const CONFIGURED = (process.env.REVIT_MCP_DYNAMO_BACKEND || "auto").toLowerCase();

export function httpBackendConfigured(): boolean {
  return HTTP_URL.length > 0;
}

export function httpBackendUrl(): string {
  return HTTP_URL;
}

async function postJson(url: string, body: unknown, timeoutMs: number): Promise<{ status: number; json: any }> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await response.text();
    let json: any;
    try {
      json = JSON.parse(text);
    } catch {
      json = { raw: text.slice(0, 2000) };
    }
    return { status: response.status, json };
  } finally {
    clearTimeout(timer);
  }
}

async function callHttp(op: string, payload: Record<string, unknown>, timeoutMs: number): Promise<BackendResult> {
  if (!HTTP_URL) {
    throw new Error(
      "No external Dynamo bridge is configured. Set REVIT_MCP_DYNAMO_HTTP_URL to one that accepts " +
        'POST {"op": "..."} and answers {"ok": true|false}.'
    );
  }
  const r = await postJson(HTTP_URL, { op, ...payload }, timeoutMs);
  const data = r.json;
  return { backend: "http", ok: r.status === 200 && data?.ok !== false, op, data };
}

async function callNative(op: string, payload: Record<string, unknown>, timeoutMs: number): Promise<BackendResult> {
  // The command set honours timeoutMs for "run" (it used to accept and ignore the
  // tool's timeout_seconds); the socket client's own 2-minute timeout is raised
  // to match so a long run is not cut off on this side first.
  const data = await withRevitConnection(async (client) =>
    client.sendCommand("dynamo_op", { op, timeoutMs, ...payload }, Math.max(timeoutMs + 5000, 120000))
  );
  const record = data as { ok?: boolean } | null;
  return { backend: "native", ok: record?.ok !== false, op, data };
}

/**
 * Route one op to whichever backend is available. When nothing answers, the
 * error names every path that was tried and what each needs.
 */
export async function dynamoOp(op: string, payload: Record<string, unknown> = {}, timeoutMs = 120000): Promise<BackendResult> {
  const attempts: string[] = [];

  if (CONFIGURED === "http" || (CONFIGURED === "auto" && HTTP_URL)) {
    try {
      return await callHttp(op, payload, timeoutMs);
    } catch (error) {
      if (CONFIGURED === "http") throw error;
      attempts.push(`http (${HTTP_URL}): ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  if (CONFIGURED === "native" || CONFIGURED === "auto") {
    try {
      return await callNative(op, payload, timeoutMs);
    } catch (error) {
      if (CONFIGURED === "native") throw error;
      attempts.push(`native: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  throw new Error(
    `No live Dynamo backend answered "${op}".\n` +
      attempts.map((a) => `  - ${a}`).join("\n") +
      `\n\nStart Revit with the mcp-servers-for-revit add-in loaded (it starts its server automatically; ` +
      `otherwise switch on "Revit MCP Switch" on the Add-Ins ribbon). If you run your own bridge into Revit, ` +
      `point REVIT_MCP_DYNAMO_HTTP_URL at it. Set REVIT_MCP_DYNAMO_BACKEND=native|http to pin one.`
  );
}

/** Read-only status query. */
export async function dynamoStatus(): Promise<BackendResult & { data: DynamoStatusData }> {
  const r = await dynamoOp("status", {}, 30000);
  return { ...r, data: (r.data ?? {}) as DynamoStatusData };
}

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

export interface ReadyResult {
  reachable: boolean;
  launched: boolean;
  waitedMs: number;
  status: DynamoStatusData;
  launch?: unknown;
}

/**
 * Make sure Dynamo's model is reachable, starting Dynamo when allowed.
 *
 * Returns as soon as `status` reports the model, or after `timeoutMs` (default
 * 90 s — Dynamo start-up is 10–30 s on a typical workstation, longer on a cold
 * disk cache). The poll interval is deliberately coarse: each poll is a socket
 * round-trip into Revit's API thread.
 */
export async function ensureDynamoReady(options: { launch: boolean; timeoutMs?: number }): Promise<ReadyResult> {
  const timeoutMs = options.timeoutMs ?? 90000;
  const started = Date.now();
  const first = await dynamoStatus();
  if (first.data.model_reachable) return { reachable: true, launched: false, waitedMs: 0, status: first.data };
  if (first.data.loaded === false) {
    return { reachable: false, launched: false, waitedMs: Date.now() - started, status: first.data };
  }
  if (!options.launch) return { reachable: false, launched: false, waitedMs: Date.now() - started, status: first.data };

  const launch = await dynamoOp("launch", {}, 30000);
  if (!launch.ok) {
    return { reachable: false, launched: false, waitedMs: Date.now() - started, status: first.data, launch: launch.data };
  }

  let last = first.data;
  while (Date.now() - started < timeoutMs) {
    await sleep(2500);
    try {
      last = (await dynamoStatus()).data;
    } catch (error) {
      last = { message: error instanceof Error ? error.message : String(error) };
    }
    if (last.model_reachable) {
      return { reachable: true, launched: true, waitedMs: Date.now() - started, status: last, launch: launch.data };
    }
  }
  return { reachable: false, launched: true, waitedMs: Date.now() - started, status: last, launch: launch.data };
}

/** Report which backends are reachable, without performing any action. */
export async function probeBackends(): Promise<{
  configured: string;
  native: { reachable: boolean; detail?: string };
  http: { configured: boolean; url: string; reachable: boolean; detail?: string };
}> {
  const out = {
    configured: CONFIGURED,
    native: { reachable: false } as { reachable: boolean; detail?: string },
    http: { configured: httpBackendConfigured(), url: HTTP_URL, reachable: false } as {
      configured: boolean;
      url: string;
      reachable: boolean;
      detail?: string;
    },
  };

  try {
    // Connect and immediately release: establishing the socket is the whole question.
    await withRevitConnection(async () => true);
    out.native.reachable = true;
  } catch (error) {
    out.native.detail = error instanceof Error ? error.message : String(error);
  }

  if (HTTP_URL) {
    try {
      const r = await postJson(HTTP_URL, { op: "status" }, 3000);
      out.http.reachable = r.status === 200;
      if (!out.http.reachable) out.http.detail = `responded HTTP ${r.status}`;
    } catch (error) {
      out.http.detail = error instanceof Error ? error.message : String(error);
    }
  } else {
    out.http.detail = "not configured (REVIT_MCP_DYNAMO_HTTP_URL is unset)";
  }

  return out;
}
