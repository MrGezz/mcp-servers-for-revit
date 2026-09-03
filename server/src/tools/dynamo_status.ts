import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ensureDynamoReady, probeBackends } from "../dynamo/backend.js";
import { errorMessage, fail, ok } from "../utils/reply.js";

export function registerDynamoStatusTool(server: McpServer) {
  server.tool(
    "dynamo_status",
    "Is Dynamo usable inside the running Revit? Reports whether the add-in socket answers, whether Dynamo is installed, whether its model is running and which graph is open. With launch:true it starts Dynamo (same as clicking Manage > Dynamo) and waits until it is ready. Read-only otherwise.",
    {
      launch: z.boolean().optional().describe("Start Dynamo if installed but not running (default false)"),
      timeout_seconds: z.number().int().positive().max(600).optional().describe("How long to wait for start-up (default 90)"),
    },
    async (args) => {
      const probe = await probeBackends();
      if (!probe.native.reachable && !probe.http.reachable) {
        return fail("No live backend: Dynamo cannot be driven right now.", {
          native: probe.native.detail,
          http: probe.http.detail,
          note: "dynamo_read_graph, dynamo_list_graphs and dynamo_edit_graph still work: they need neither Revit nor Dynamo.",
        });
      }
      try {
        const ready = await ensureDynamoReady({ launch: args.launch === true, timeoutMs: (args.timeout_seconds ?? 90) * 1000 });
        const s = ready.status;
        const summary = {
          ok: true,
          installed: s.loaded !== false,
          running: ready.reachable,
          launched_now: ready.launched,
          waited_ms: ready.waitedMs || undefined,
          current_workspace: s.current_workspace ?? undefined,
          backend: probe.native.reachable ? "native" : "http",
          message: ready.reachable
            ? "Dynamo is running; dynamo_run_graph can open and run graphs."
            : s.loaded === false
              ? "Dynamo for Revit is not installed in this Revit."
              : args.launch
                ? "Dynamo did not become ready in time. Check Revit for a dialog (e.g. Dynamo version selector) and retry."
                : "Dynamo is installed but not running. Call dynamo_status with launch:true, or dynamo_run_graph which launches it automatically.",
        };
        return ok(summary);
      } catch (error) {
        return fail(`dynamo_status failed: ${errorMessage(error)}`);
      }
    }
  );
}
