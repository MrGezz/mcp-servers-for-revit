import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { probeBackends, dynamoOp } from "../dynamo/backend.js";

export function registerDynamoStatusTool(server: McpServer) {
  server.tool(
    "dynamo_status",
    "Report whether Dynamo can be driven inside the running Revit: which backend is reachable, " +
      "whether the Dynamo assemblies are loaded, whether its model is reachable, and which graph is " +
      "currently open. Read-only and safe to call at any time — call it before dynamo_run_graph.",
    {},
    async () => {
      const lines: string[] = [];

      const probe = await probeBackends();
      lines.push("## Backends");
      lines.push(`configured: ${probe.configured}`);
      lines.push(
        `native (plugin socket): ${probe.native.reachable ? "reachable" : "not reachable"}` +
          (probe.native.detail ? ` — ${probe.native.detail}` : "")
      );
      lines.push(
        `http (external bridge): ${
          !probe.http.configured ? "not configured" : probe.http.reachable ? `reachable at ${probe.http.url}` : "not reachable"
        }` + (probe.http.detail ? ` — ${probe.http.detail}` : "")
      );

      if (!probe.native.reachable && !probe.http.reachable) {
        lines.push(
          "",
          "No live backend, so Dynamo cannot be driven right now. Reading and editing .dyn files " +
            "still works — dynamo_read_graph, dynamo_list_graphs and dynamo_edit_graph need neither " +
            "Revit nor Dynamo."
        );
        return { content: [{ type: "text", text: lines.join("\n") }] };
      }

      try {
        const result = await dynamoOp("status", {}, 30000);
        lines.push("", `## Dynamo (via the ${result.backend} backend)`);
        lines.push(JSON.stringify(result.data, null, 2));
        lines.push(
          "",
          "Note: Dynamo's assemblies are in the Revit process from startup, so a report of " +
            "'loaded' with an unreachable model is the ordinary state before Dynamo has been " +
            "opened once from the Manage ribbon — not a fault."
        );
      } catch (error) {
        lines.push("", "## Dynamo status query failed", error instanceof Error ? error.message : String(error));
      }

      return { content: [{ type: "text", text: lines.join("\n") }] };
    }
  );
}
