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
 * THE HTTP CONTRACT, IF YOU IMPLEMENT ONE
 * ---------------------------------------------------------------------------
 * Deliberately tiny, so it can sit in front of anything:
 *
 *   POST <REVIT_MCP_DYNAMO_HTTP_URL>
 *   Content-Type: application/json
 *   { "op": "status" | "open" | "run", ...operation arguments }
 *
 *   200 { "ok": true,  ... }        the operation succeeded
 *       { "ok": false, "message": "..." }   it did not
 *
 * `op` carries the verb rather than the URL path, which is what lets the two
 * backends be swapped without the tools knowing which is in play.
 *
 * ---------------------------------------------------------------------------
 * WHY THERE IS NO DRY RUN
 * ---------------------------------------------------------------------------
 * Every other write in this project can be reasoned about as "do the thing, and
 * a transaction can undo it". A Dynamo graph cannot. It opens and commits its
 * own transactions, in its own order, and a run that half-completes leaves the
 * document in whatever state the graph reached. So `run` takes an explicit
 * confirm rather than pretending a rollback exists.
 */

import { withRevitConnection } from "../utils/ConnectionManager.js";

export type BackendName = "native" | "http";

export interface BackendResult {
  backend: BackendName;
  ok: boolean;
  op: string;
  data: unknown;
}

/**
 * An external Dynamo bridge, if the operator has one. Empty by default: with no
 * URL configured the "http" backend is simply absent, and `auto` resolves to
 * native only. No host, port or path is guessed — a bridge nobody configured is
 * not a bridge, and probing invented addresses produces confusing failures.
 */
const HTTP_URL = process.env.REVIT_MCP_DYNAMO_HTTP_URL || "";

/** "auto" (default) | "native" | "http" */
const CONFIGURED: string = (process.env.REVIT_MCP_DYNAMO_BACKEND || "auto").toLowerCase();

export function httpBackendConfigured(): boolean {
  return HTTP_URL.length > 0;
}

export function httpBackendUrl(): string {
  return HTTP_URL;
}

async function postJson(url: string, body: unknown, timeoutMs: number): Promise<{ status: number; json: unknown }> {
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
    let json: unknown;
    try {
      json = JSON.parse(text);
    } catch {
      // A bridge that answers with something other than JSON is misconfigured
      // rather than absent; keep the body so the error can say what came back.
      json = { raw: text.slice(0, 2000) };
    }
    return { status: response.status, json };
  } finally {
    clearTimeout(timer);
  }
}

/** Send one op to the configured external bridge. */
async function callHttp(op: string, payload: Record<string, unknown>, timeoutMs: number): Promise<BackendResult> {
  if (!HTTP_URL) {
    throw new Error(
      "No external Dynamo bridge is configured. Set REVIT_MCP_DYNAMO_HTTP_URL to one that accepts " +
        'POST {"op": "..."} and answers {"ok": true|false}.'
    );
  }
  const r = await postJson(HTTP_URL, { op, ...payload }, timeoutMs);
  const data = r.json as Record<string, unknown> | null;
  return { backend: "http", ok: r.status === 200 && data?.ok !== false, op, data };
}

/**
 * Send one op down this project's own plugin socket.
 *
 * The command set answers `dynamo_op` with the op in the parameters, mirroring
 * the single-verb shape the HTTP contract uses, so the two backends stay
 * swappable without the tools knowing which is in play.
 */
async function callNative(op: string, payload: Record<string, unknown>): Promise<BackendResult> {
  const data = await withRevitConnection(async (client) => client.sendCommand("dynamo_op", { op, ...payload }));
  const record = data as Record<string, unknown> | null;
  return { backend: "native", ok: record?.ok !== false, op, data };
}

/**
 * Route one op to whichever backend is available.
 *
 * Loud failure, not silent degradation: when nothing answers, the error names
 * every path that was tried and what each needs. "Dynamo is unavailable" without
 * saying which channel was attempted is the least actionable message possible.
 */
export async function dynamoOp(
  op: string,
  payload: Record<string, unknown> = {},
  timeoutMs = 120000
): Promise<BackendResult> {
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
      return await callNative(op, payload);
    } catch (error) {
      if (CONFIGURED === "native") throw error;
      attempts.push(`native: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  throw new Error(
    `No live Dynamo backend answered "${op}".\n` +
      attempts.map((a) => `  - ${a}`).join("\n") +
      `\n\nStart Revit with the mcp-servers-for-revit add-in loaded and the "Revit MCP Switch" ` +
      `turned on. If you run your own bridge into Revit instead, point REVIT_MCP_DYNAMO_HTTP_URL at it. ` +
      `Set REVIT_MCP_DYNAMO_BACKEND=native|http to pin one and get its error directly rather than this summary.`
  );
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
    // Connect and immediately release. Deliberately sends NO command: say_hello
    // opens a dialog in Revit and get_current_view_info needs an active view, so
    // either would make a "is anything listening" probe intrusive or wrong.
    // Establishing the socket is the whole question being asked.
    await withRevitConnection(async () => true);
    out.native.reachable = true;
  } catch (error) {
    out.native.detail = error instanceof Error ? error.message : String(error);
  }

  if (HTTP_URL) {
    try {
      // `status` is the probe because it is the one op guaranteed read-only.
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
