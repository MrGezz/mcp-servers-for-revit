import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { memoryOp, projectId } from "../memory/legacyBridge.js";

export function registerQueryStoredDataTool(server: McpServer) {
  server.tool(
    "query_stored_data",
    "Query project and room data stored in the current Revit model. Reports how many entities were " +
      "SEARCHED as well as how many matched, so 'no results' from an empty store is distinguishable " +
      "from a genuine miss - the previous version could not tell those apart, and would report " +
      "success for data it had never stored.",
    {
      query_type: z
        .enum([
          "all_projects",
          "project_by_name",
          "rooms_by_project_name",
          "all_rooms",
          "stats",
        ])
        .describe("Type of query to perform"),
      project_name: z
        .string()
        .optional()
        .describe("Project name (required for 'project_by_name' and 'rooms_by_project_name')"),
    },
    async (args: any) => {
      const need = (v: string | undefined, why: string) => {
        if (!v) throw new Error(`project_name is required for ${why}`);
        return v;
      };
      try {
        let response: unknown;
        switch (args.query_type) {
          case "all_projects":
            response = await memoryOp("query", { kind: "project", limit: 500 });
            break;
          case "project_by_name":
            response = await memoryOp("query", {
              kind: "project",
              name: need(args.project_name, "project_by_name"),
            });
            break;
          case "all_rooms":
            response = await memoryOp("query", { kind: "room", limit: 500 });
            break;
          case "rooms_by_project_name": {
            // Rooms are reached through the project's 'contains' edges rather than by
            // a name convention, so a renamed room is still found.
            const pid = projectId(need(args.project_name, "rooms_by_project_name"));
            response = await memoryOp("query", { name: pid, relation: "contains", limit: 500 });
            break;
          }
          case "stats":
            response = await memoryOp("stats", {});
            break;
          default:
            throw new Error(`Unknown query_type: ${args.query_type}`);
        }
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return {
          content: [
            {
              type: "text" as const,
              text:
                "query_stored_data failed: " +
                (error instanceof Error ? error.message : String(error)) +
                "\n\nThis tool now reads from the open Revit model, so it needs a live connection " +
                "and an open document.",
            },
          ],
          isError: true as const,
        };
      }
    }
  );
}
