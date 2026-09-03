import { z } from "zod";
import { access } from "node:fs/promises";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { dynamoOp, ensureDynamoReady } from "../dynamo/backend.js";
import { errorMessage, fail, ok } from "../utils/reply.js";

interface ProblemNode {
  name: string;
  id?: string;
  state: string;
  messages: string[];
}

interface EvaluationReport {
  completed: boolean;
  waited_ms: number;
  evaluation_count?: number | null;
  has_run_without_crash?: boolean | null;
  node_count?: number;
  problem_nodes: ProblemNode[];
  note?: string;
}

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/**
 * Wait for the run to finish and say how it went.
 *
 * "run" returns as soon as Dynamo has been told to evaluate, with the
 * workspace's EvaluationCount as it was before. This polls eval_status until
 * that count moves, then hands back every node that did not end Active. The
 * wait is on this side, not inside Revit: see DynamoEventHandler.EvalStatus
 * for why a synchronous wait in the external event would deadlock.
 *
 * An add-in older than 1.0.2 has no evaluation_count_before; the report then
 * says so instead of pretending the run succeeded.
 */
async function awaitEvaluation(runData: unknown, timeoutMs: number): Promise<EvaluationReport> {
  const before = (runData as { evaluation_count_before?: number | null } | null)?.evaluation_count_before;
  const report: EvaluationReport = { completed: false, waited_ms: 0, problem_nodes: [] };
  if (typeof before !== "number") {
    report.note =
      "This add-in build does not report evaluation state (update the add-in). " +
      "Completion is not confirmed; check the Dynamo window for node warnings.";
    return report;
  }

  const started = Date.now();
  const budget = Math.max(timeoutMs - 2000, 5000);
  let last: Record<string, unknown> | null = null;
  while (Date.now() - started < budget) {
    await sleep(1000);
    const r = await dynamoOp("eval_status", {}, 30000);
    last = (r.data ?? null) as Record<string, unknown> | null;
    if (r.ok && typeof last?.evaluation_count === "number" && last.evaluation_count > before) break;
  }
  report.waited_ms = Date.now() - started;

  const count = last?.evaluation_count;
  if (typeof count !== "number" || count <= before) {
    report.note = "Dynamo had not finished evaluating within timeout_seconds; check the Dynamo window, or call again later.";
    return report;
  }
  report.completed = true;
  report.evaluation_count = count;
  report.has_run_without_crash = (last?.has_run_without_crash as boolean | null | undefined) ?? null;
  report.node_count = last?.node_count as number | undefined;
  report.problem_nodes = Array.isArray(last?.problem_nodes) ? (last!.problem_nodes as ProblemNode[]) : [];
  return report;
}

export function registerDynamoRunGraphTool(server: McpServer) {
  server.tool(
    "dynamo_run_graph",
    "Open a .dyn graph in the running Revit and optionally RUN it. Starts Dynamo automatically if it is not running (10-30 s). After a run it waits for the evaluation and reports any node that ended in a warning or error state (a Python exception, say) as an error. Running modifies the model and cannot be undone by this server (the graph commits its own transactions), so run:true also requires confirm:true. Use dynamo_read_graph first to see what a graph does.",
    {
      path: z.string().describe("Absolute path to the .dyn file"),
      run: z.boolean().optional().describe("Run after opening (default false = open only, always safe)"),
      confirm: z.boolean().optional().describe("Must be true to run"),
      timeout_seconds: z.number().int().positive().max(900).optional().describe("Wait for the run (default 300)"),
      autoLaunch: z.boolean().optional().describe("Start Dynamo when needed (default true)"),
    },
    async (args) => {
      try {
        try {
          await access(args.path);
        } catch {
          return fail(`No file at ${args.path}.`);
        }
        if (!/\.dyn$/i.test(args.path)) {
          return fail(`${args.path} is not a .dyn file. A .dyf is a custom node definition and cannot be run directly.`);
        }
        if (args.run && args.confirm !== true) {
          return fail("REFUSED: running a Dynamo graph is not reversible. Re-issue with confirm:true if the user agreed.", {
            needs_confirm: true,
            hint: "To inspect first: dynamo_read_graph, or call this tool with run:false to open it in Dynamo only.",
          });
        }

        const ready = await ensureDynamoReady({ launch: args.autoLaunch !== false });
        if (!ready.reachable) {
          return fail(
            ready.status.loaded === false
              ? "Dynamo for Revit is not installed in this Revit."
              : ready.launched
                ? "Dynamo was started but did not become ready in time. Check Revit for a dialog, then retry."
                : "Dynamo is not running. Call again with autoLaunch:true (default) or dynamo_status {launch:true}.",
            { launch: ready.launch, status: ready.status }
          );
        }

        const timeoutMs = Math.min(Math.max(args.timeout_seconds ?? 300, 5), 900) * 1000;
        const opened = await dynamoOp("open", { path: args.path }, timeoutMs);
        if (!opened.ok) return fail("Open did not report success, so the graph was NOT run.", { open: opened.data });

        if (!args.run) {
          return ok({ ok: true, launched: ready.launched, opened: opened.data, note: "Opened only, not run (pass run:true and confirm:true to run)." });
        }

        const ran = await dynamoOp("run", { path: args.path, confirm: true }, timeoutMs);
        if (!ran.ok) return fail("Run did not report success.", { open: opened.data, run: ran.data });

        const evaluation = await awaitEvaluation(ran.data, timeoutMs);
        const result = { ok: true, launched: ready.launched, opened: opened.data, run: ran.data, evaluation };
        // A node that ended in Warning/Error produced nothing, and a model that
        // reads a plain ok:true reports the job as done. Dead (an unconnected
        // input) is listed but not fatal: it is how an optional input looks.
        const failed = evaluation.problem_nodes.filter((n) => n.state !== "Dead");
        // No completion report means the outcome is UNKNOWN, not good: the run was
        // requested, but whether it finished, failed, or is still going cannot be
        // told from here. Saying ok would let a model report a job as done.
        if (!evaluation.completed) {
          return fail(
            (evaluation.note ?? "Dynamo did not report the evaluation as finished.") +
              " The run was requested and may still be in progress; check the Dynamo window, or call again with a larger timeout_seconds.",
            result
          );
        }
        if (failed.length) {
          return fail(
            `The graph ran but ${failed.length} node(s) failed: ` +
              failed.map((n) => `${n.name} [${n.state}] ${n.messages.join(" | ")}`).join("; "),
            result
          );
        }
        return ok(result);
      } catch (error) {
        return fail(`dynamo_run_graph failed: ${errorMessage(error)}`);
      }
    }
  );
}
