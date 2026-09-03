import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerManageProjectParametersTool(server: McpServer) {
  server.tool(
    "manage_project_parameters",
    "List or bind shared parameters in a Revit project. 'list' returns existing parameters; 'add' binds from a shared parameter file. Returns parameter list or bind result.",
    {
      action: z.enum(["list", "add"]).describe("'list' or 'add'"),
      sharedParamFile: z.string().optional().describe("Path to shared parameter file (required for 'add')"),
      paramGroup: z.string().default("General").describe("Shared parameter group name"),
      params: z.array(z.object({
        name: z.string(),
        categories: z.array(z.string()).optional().describe("Revit categories to bind to"),
      })).optional().describe("Parameters to add (required for 'add')"),
    },
    async (args) => {
      const params: Record<string, unknown> = {
        action: args.action,
      };
      if (args.sharedParamFile !== undefined) params.sharedParamFile = args.sharedParamFile;
      if (args.paramGroup !== undefined) params.paramGroup = args.paramGroup;
      if (args.params !== undefined) params.params = args.params;

      return callRevit("manage_project_parameters", params);
    }
  );
}
