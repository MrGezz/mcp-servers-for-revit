import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { add, get, search, stats } from "../memory/knowledge.js";
import { ingestFile } from "../memory/ingest.js";

/**
 * KNOWLEDGE MEMORY tools - the durable, package-wide half of the memory layer.
 *
 * Purpose: stop every session re-deriving what an earlier one already worked out.
 * Which node chain solves a task, which API call is right on which Revit version,
 * how one of these 90-odd tools actually behaves, what a team has standardised on -
 * all of it is stored once, namespaced, and searchable.
 *
 * This is not scoped to Dynamo, or to any one subsystem. Namespaces keep the store
 * usable across the whole package (see memory/knowledge.ts for the conventions).
 *
 * Storage is a per-user JSONL file - see memory/paths.ts for WHY it is not beside
 * the package.
 */
export function registerKnowledgeMemoryTools(server: McpServer) {
  const ok = (data: unknown) => ({
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
  });
  const bad = (e: unknown) => ({
    content: [
      {
        type: "text" as const,
        text: JSON.stringify(
          { success: false, error: e instanceof Error ? e.message : String(e) },
          null,
          2
        ),
      },
    ],
    isError: true as const,
  });

  server.tool(
    "knowledge_search",
    "Search durable knowledge memory before researching something from scratch. Covers every namespace: " +
      "dynamo.chain (node chains that solve a task), dynamo.node, revit.recipe (API sequences known to work), " +
      "command.usage (how this server's own tools behave), project.standard (team conventions), and doc.* " +
      "(anything ingested in bulk). Returns the matched terms alongside each hit so the ranking is explainable. " +
      "Ask this FIRST when a task looks like one somebody has solved before.",
    {
      query: z.string().describe("Free text. Node names, API members and tool names all work well."),
      ns: z.string().optional().describe("Restrict to a namespace prefix, e.g. 'dynamo' or 'dynamo.chain'"),
      tags: z.array(z.string()).optional().describe("Every listed tag must be present"),
      limit: z.number().optional().describe("Maximum hits (default 10)"),
    },
    async (args: any) => {
      try {
        const hits = search(args.query, { ns: args.ns, tags: args.tags, limit: args.limit });
        const s = stats();
        if (!s.units) {
          return ok({
            success: true,
            results: [],
            note:
              "Knowledge memory is EMPTY - this is not 'no match', it is 'nothing has been stored yet'. " +
              "Add units with knowledge_add, or bulk-load a reference document with knowledge_ingest.",
            store: s.file,
          });
        }
        return ok({
          success: true,
          query: args.query,
          searched: s.units,
          results: hits.map((h) => ({
            id: h.unit.id,
            ns: h.unit.ns,
            title: h.unit.title,
            tags: h.unit.tags,
            score: Number(h.score.toFixed(3)),
            matched: h.matched,
            preview: h.unit.body.length > 600 ? h.unit.body.slice(0, 600) + " ..." : h.unit.body,
            truncated: h.unit.body.length > 600,
          })),
        });
      } catch (e) {
        return bad(e);
      }
    }
  );

  server.tool(
    "knowledge_get",
    "Read one knowledge unit in full, by the id returned from knowledge_search. Use this when a search " +
      "preview was truncated.",
    { id: z.string().describe("Unit id from knowledge_search") },
    async (args: any) => {
      try {
        const u = get(args.id);
        if (!u) return ok({ success: false, error: `No knowledge unit with id ${args.id}` });
        return ok({ success: true, unit: u });
      } catch (e) {
        return bad(e);
      }
    }
  );

  server.tool(
    "knowledge_add",
    "Store one thing worth not re-deriving: a node chain that worked, an API sequence and the version it " +
      "applies to, a trap and how it presents, a convention a team has settled on. Re-adding identical " +
      "content is reported as a duplicate rather than stored twice.",
    {
      ns: z
        .string()
        .describe(
          "Namespace. Conventions: dynamo.chain, dynamo.node, revit.recipe, command.usage, project.standard, doc.<name>"
        ),
      title: z.string().describe("One line. This is what search ranks most heavily."),
      body: z.string().describe("The content. Include the failure mode, not just the happy path."),
      tags: z.array(z.string()).optional().describe("e.g. ['revit2026','mep','list-levels']"),
      source: z
        .object({
          kind: z.enum(["document", "session", "manual", "import"]),
          ref: z.string().optional(),
          locator: z.string().optional(),
        })
        .optional()
        .describe("Where this came from, so a later reader can check it"),
    },
    async (args: any) => {
      try {
        const { unit, duplicate } = add({
          ns: args.ns,
          title: args.title,
          body: args.body,
          tags: args.tags,
          source: args.source,
        });
        return ok({
          success: true,
          duplicate,
          id: unit.id,
          ns: unit.ns,
          note: duplicate ? "Identical content already stored; nothing was written." : "Stored.",
        });
      } catch (e) {
        return bad(e);
      }
    }
  );

  server.tool(
    "knowledge_ingest",
    "Bulk-load a reference document into knowledge memory, split into individually searchable units. " +
      "Accepts .md (split on headings), .txt (split on page/slide markers if present, else blank lines), " +
      ".json/.jsonl, and .csv/.tsv. PDF, DOCX and PPTX are REFUSED with conversion instructions - this " +
      "server carries no document parsers, because that would add a native dependency and because a bad " +
      "parse of a multi-column layout produces convincing nonsense.",
    {
      path: z.string().describe("Absolute path to the file"),
      ns: z.string().describe("Namespace to file every unit under, e.g. 'doc.dynamo-combinations'"),
      tags: z.array(z.string()).optional().describe("Tags applied to every unit from this file"),
      min_length: z.number().optional().describe("Skip blocks shorter than this many characters (default 40)"),
    },
    async (args: any) => {
      try {
        const r = ingestFile(args.path, args.ns, { tags: args.tags, minLength: args.min_length });
        return ok({ success: true, ...r });
      } catch (e) {
        return bad(e);
      }
    }
  );

  server.tool(
    "knowledge_stats",
    "What is in knowledge memory, and where it lives on disk. Call this when a search returns nothing, to " +
      "tell an empty store apart from a genuine miss.",
    {},
    async () => {
      try {
        return ok({ success: true, ...stats() });
      } catch (e) {
        return bad(e);
      }
    }
  );
}
