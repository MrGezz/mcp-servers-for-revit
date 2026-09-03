import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Limit } from "../utils/schemas.js";

export function registerGetAvailableFamilyTypesTool(server: McpServer) {
  server.tool(
    "get_available_family_types",
    "Returns family types in the current project. Filter by Revit category names (e.g. OST_Walls) and/or family name substring. Returns names, ids, and categories.",
    {
      categoryList: z
        .array(z.string())
        .optional()
        .describe("Revit category names to filter by (e.g. OST_Walls)"),
      familyNameFilter: z
        .string()
        .optional()
        .describe("Family name substring filter"),
      limit: Limit(30),
    },
    async (args) =>
      callRevit("get_available_family_types", {
        categoryList: args.categoryList || [],
        familyNameFilter: args.familyNameFilter || "",
        limit: args.limit || 30,
      })
  );
}
