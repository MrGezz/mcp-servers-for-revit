import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { ok, fail, errorMessage } from "../utils/reply.js";
import { add, get, search, stats } from "../memory/knowledge.js";
import { ingestFile } from "../memory/ingest.js";

export function registerKnowledgeMemoryTools(server: McpServer) {
  server.tool(
    "knowledge_search",
    "Search durable knowledge memory. Namespaces: dynamo.chain, dynamo.node, revit.recipe, command.usage, project.standard, doc.*. Returns hits with matched terms and ranking. Call before re-deriving a known solution.",
    {
      query: z.string().describe("Free-text query; node names, API members, tool names work"),
      ns: z.string().optional().describe("Namespace prefix to restrict, e.g. 'dynamo.chain'"),
      tags: z.array(z.string()).optional().describe("Every listed tag must be present"),
      limit: z.number().optional().describe("Maximum hits (default 10)"),
    },
    async (args) => {
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
        return fail(errorMessage(e));
      }
    }
  );

  server.tool(
    "knowledge_get",
    "Read one knowledge unit in full by the id returned from knowledge_search. Use when a search preview was truncated.",
    { id: z.string().describe("Unit id from knowledge_search") },
    async (args) => {
      try {
        const u = get(args.id);
        if (!u) return fail(`No knowledge unit with id ${args.id}`);
        return ok({ success: true, unit: u });
      } catch (e) {
        return fail(errorMessage(e));
      }
    }
  );

  server.tool(
    "knowledge_add",
    "Store a reusable insight: a node chain, API sequence, trap, or team convention. Duplicate content is reported, not stored twice.",
    {
      ns: z.string().describe("e.g. dynamo.chain, revit.recipe, project.standard"),
      title: z.string().describe("One line; search ranks this most heavily"),
      body: z.string().describe("Full content; include failure modes"),
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
    async (args) => {
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
        return fail(errorMessage(e));
      }
    }
  );

  server.tool(
    "knowledge_ingest",
    "Bulk-load a file into knowledge memory as searchable units. Accepts .md, .txt, .json, .jsonl, .csv, .tsv. PDF/DOCX/PPTX are refused with conversion instructions.",
    {
      path: z.string().describe("Absolute path to the file"),
      ns: z.string().describe("Namespace for every unit, e.g. 'doc.dynamo-combinations'"),
      tags: z.array(z.string()).optional().describe("Tags applied to every unit from this file"),
      min_length: z.number().optional().describe("Skip blocks shorter than this many chars (default 40)"),
    },
    async (args) => {
      try {
        const r = ingestFile(args.path, args.ns, { tags: args.tags, minLength: args.min_length });
        return ok({ success: true, ...r });
      } catch (e) {
        return fail(errorMessage(e));
      }
    }
  );

  server.tool(
    "knowledge_stats",
    "What is in knowledge memory, and where it lives on disk. Call when a search returns nothing to tell an empty store apart from a genuine miss.",
    {},
    async () => {
      try {
        return ok({ success: true, ...stats() });
      } catch (e) {
        return fail(errorMessage(e));
      }
    }
  );
}
