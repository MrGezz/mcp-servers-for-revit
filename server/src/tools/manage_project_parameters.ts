import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageProjectParametersTool(server: McpServer) {
  server.tool(
    "manage_project_parameters",
    "List or add shared parameters to a Revit project. Supports listing existing project parameters and binding new shared parameters from a shared parameter file.",
    {
      action: z.enum(["list", "add"]).describe("Action: 'list' to show existing project parameters, 'add' to bind shared parameters"),
      sharedParamFile: z.string().optional().describe("Path to shared parameter file (required for 'add' action)"),
      paramGroup: z.string().default("General").describe("Shared parameter group name (default: 'General')"),
      params: z.array(z.object({
        name: z.string().describe("Shared parameter name"),
        categories: z.array(z.string()).optional().describe("Revit categories to bind this parameter to"),
      })).optional().describe("List of shared parameters to add (required for 'add' action)"),
    },
    async (args, extra) => {
      const params: any = {
        action: args.action,
      };
      if (args.sharedParamFile !== undefined) params.sharedParamFile = args.sharedParamFile;
      if (args.paramGroup !== undefined) params.paramGroup = args.paramGroup;
      if (args.params !== undefined) params.params = args.params;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_project_parameters", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Manage project parameters failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
