import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { memoryOp, projectId } from "../memory/legacyBridge.js";
import { fail, fromRevit, errorMessage } from "../utils/reply.js";

export function registerQueryStoredDataTool(server: McpServer) {
  server.tool(
    "query_stored_data",
    "Query project and room data stored in the Revit model. project_name is required for project_by_name and rooms_by_project_name. Returns matched records or stats; searched vs matched counts distinguish an empty store from a genuine miss.",
    {
      query_type: z.enum([
        "all_projects",
        "project_by_name",
        "rooms_by_project_name",
        "all_rooms",
        "stats",
      ]),
      project_name: z
        .string()
        .optional()
        .describe("Required for project_by_name and rooms_by_project_name"),
    },
    async (args) => {
      try {
        let response: unknown;
        switch (args.query_type) {
          case "all_projects":
            response = await memoryOp("query", { kind: "project", limit: 500 });
            break;
          case "project_by_name":
            if (!args.project_name)
              return fail("project_name is required for project_by_name");
            response = await memoryOp("query", {
              kind: "project",
              name: args.project_name,
            });
            break;
          case "all_rooms":
            response = await memoryOp("query", { kind: "room", limit: 500 });
            break;
          case "rooms_by_project_name": {
            if (!args.project_name)
              return fail("project_name is required for rooms_by_project_name");
            const pid = projectId(args.project_name);
            // Step 1: get the project entity and the "contains" relations it participates in.
            // Querying by name=pid matches the project entity (its id IS pid).
            const projRaw = await memoryOp("query", { name: pid, limit: 1 });
            const projData: any =
              (projRaw as any)?.Response ?? (projRaw as any)?.response ?? projRaw;
            const containsRels: any[] = Array.isArray(projData?.relations)
              ? projData.relations.filter(
                  (r: any) => r.from === pid && r.kind === "contains"
                )
              : [];
            const roomIds = new Set<string>(
              containsRels.map((r: any) => String(r.to))
            );
            // Step 2: get all room entities and keep only those linked to this project.
            const roomsRaw = await memoryOp("query", { kind: "room", limit: 500 });
            const roomsData: any =
              (roomsRaw as any)?.Response ?? (roomsRaw as any)?.response ?? roomsRaw;
            const filteredRooms: any[] = Array.isArray(roomsData?.entities)
              ? roomsData.entities.filter((e: any) => roomIds.has(String(e.id)))
              : [];
            response = {
              entities: filteredRooms,
              relations: containsRels,
              searched: typeof roomsData?.searched === "number" ? roomsData.searched : 0,
            };
            break;
          }
          case "stats":
            response = await memoryOp("stats", {});
            break;
          default:
            return fail(`Unknown query_type: ${(args as { query_type: string }).query_type}`);
        }
        return fromRevit(response);
      } catch (error) {
        return fail(
          `query_stored_data failed: ${errorMessage(error)}`,
          { hint: "Needs a live Revit connection and an open document." }
        );
      }
    }
  );
}
