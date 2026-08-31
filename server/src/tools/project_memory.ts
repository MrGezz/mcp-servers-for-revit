import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

/**
 * PROJECT MEMORY tools - the model-scoped half of the memory layer.
 *
 * These store an entity/relation graph INSIDE the Revit document, using Extensible
 * Storage, so it travels with the model through Save As, worksharing and
 * transmittal instead of desynchronising from it.
 *
 * They replace the flat store_project_data / store_room_data / query_stored_data
 * path, which wrote a SQLite file resolved relative to the package directory - and
 * under the documented `npx -y` launch command, that directory lives in the npm
 * cache, which is disposable. "Persisted" data was sitting in a folder npm may
 * clear at any time.
 */
export function registerProjectMemoryTools(server: McpServer) {
  const call = async (action: string, payload: unknown) =>
    withRevitConnection(async (revitClient) =>
      revitClient.sendCommand("project_memory_op", { action, payload })
    );

  const reply = (data: unknown) => ({
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
  });
  const fail = (what: string, error: unknown) => ({
    content: [
      {
        type: "text" as const,
        text: `${what} failed: ${error instanceof Error ? error.message : String(error)}`,
      },
    ],
    isError: true as const,
  });

  const entitySchema = z.object({
    id: z.string().describe("Stable caller-chosen id, e.g. 'room:L1-101' or 'decision:core-wall-type'"),
    kind: z.string().describe("What sort of thing this is, e.g. room, material, decision, standard"),
    name: z.string().optional(),
    elementId: z.number().optional().describe("The Revit element this describes, when it describes one"),
    props: z.record(z.string()).optional().describe("Free-form string properties"),
  });

  const relationSchema = z.object({
    from: z.string().describe("Entity id"),
    to: z.string().describe("Entity id"),
    kind: z.string().describe("Edge type, e.g. contains, serves, supersedes"),
    props: z.record(z.string()).optional(),
  });

  server.tool(
    "project_memory_write",
    "Record facts about THIS model, stored inside the model itself via Revit Extensible Storage so " +
      "they travel with it. Entities are upserted by id; relations whose endpoints are not in the " +
      "graph are reported as dangling rather than silently stored. Use this instead of assuming a " +
      "note will still be there next session.",
    {
      entities: z.array(entitySchema).optional().describe("Entities to insert or update"),
      relations: z.array(relationSchema).optional().describe("Edges between entity ids"),
    },
    async (args: any) => {
      try {
        return reply(await call("write", { entities: args.entities ?? [], relations: args.relations ?? [] }));
      } catch (e) {
        return fail("project_memory_write", e);
      }
    }
  );

  server.tool(
    "project_memory_query",
    "Search what has been recorded about this model. Reports how many entities were SEARCHED as well " +
      "as how many matched, so an empty store is distinguishable from a genuine miss.",
    {
      kind: z.string().optional().describe("Restrict to one entity kind"),
      name: z.string().optional().describe("Substring match against name and id"),
      relation: z.string().optional().describe("Restrict returned edges to this type"),
      limit: z.number().optional().describe("Maximum entities (default 100)"),
    },
    async (args: any) => {
      try {
        return reply(await call("query", args));
      } catch (e) {
        return fail("project_memory_query", e);
      }
    }
  );

  server.tool(
    "project_memory_stats",
    "What this model's memory holds: entity and relation counts broken down by kind, and where it is " +
      "stored. Call this before concluding that a query found nothing.",
    {},
    async () => {
      try {
        return reply(await call("stats", {}));
      } catch (e) {
        return fail("project_memory_stats", e);
      }
    }
  );

  server.tool(
    "project_memory_clear",
    "Remove all project memory from the current model. This deletes the Extensible Storage entity and " +
      "cannot be undone except by Revit's own undo stack.",
    {
      confirm: z
        .boolean()
        .describe("Must be true. Present so a clear cannot happen through an unqualified call."),
    },
    async (args: any) => {
      try {
        if (args.confirm !== true) {
          return reply({
            success: false,
            error: "Refused: project_memory_clear requires confirm=true.",
          });
        }
        return reply(await call("clear", {}));
      } catch (e) {
        return fail("project_memory_clear", e);
      }
    }
  );
}
