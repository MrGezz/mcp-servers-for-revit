import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { fromRevit, fail, errorMessage } from "../utils/reply.js";

export function registerProjectMemoryTools(server: McpServer) {
  const callMemory = async (action: string, payload: unknown) => {
    try {
      const result = await withRevitConnection(async (client) =>
        client.sendCommand("project_memory_op", { action, payload })
      );
      return fromRevit(result);
    } catch (e) {
      return fail(`project_memory_op/${action} failed: ${errorMessage(e)}`);
    }
  };

  const entitySchema = z.object({
    id: z.string().describe("Stable caller-chosen id, e.g. 'room:L1-101'"),
    kind: z.string().describe("Entity type, e.g. room, material, decision"),
    name: z.string().optional(),
    elementId: z.number().optional().describe("Revit ElementId this entity describes"),
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
    "Upsert entities and edges into this model's Extensible Storage graph so facts persist with the model. Entities are upserted by id; dangling relation endpoints are reported.",
    {
      entities: z.array(entitySchema).optional().describe("Entities to insert or update"),
      relations: z.array(relationSchema).optional().describe("Edges between entity ids"),
    },
    async (args) =>
      callMemory("write", { entities: args.entities ?? [], relations: args.relations ?? [] })
  );

  server.tool(
    "project_memory_query",
    "Query entities and edges stored in this model's Extensible Storage graph. Returns match count alongside total searched, so an empty store is distinguishable from a miss.",
    {
      kind: z.string().optional().describe("Restrict to one entity kind"),
      name: z.string().optional().describe("Substring match against name and id"),
      relation: z.string().optional().describe("Restrict returned edges to this type"),
      limit: z.number().optional().describe("Max entities returned (default 100)"),
    },
    async (args) => callMemory("query", args)
  );

  server.tool(
    "project_memory_stats",
    "Return entity and relation counts by kind from this model's Extensible Storage graph. Check before concluding a query found nothing.",
    {},
    async () => callMemory("stats", {})
  );

  server.tool(
    "project_memory_clear",
    "Delete all project memory from this model's Extensible Storage. Requires confirm=true. Cannot be undone except via Revit's undo stack.",
    {
      confirm: z.boolean().describe("Must be true to proceed"),
    },
    async (args) => {
      if (args.confirm !== true) return fail("project_memory_clear requires confirm=true");
      return callMemory("clear", {});
    }
  );
}
