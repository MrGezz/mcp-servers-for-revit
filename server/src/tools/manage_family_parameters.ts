import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageFamilyParametersTool(server: McpServer) {
  server.tool(
    "manage_family_parameters",
    "Add, rename, remove, or set formulas for family parameters in a Revit family document.",
    {
      action: z.enum(["add", "rename", "remove", "set_formula"]).describe("Action to perform: add, rename, remove, set_formula"),
      familyId: z.number().int().describe("The family element ID"),
      name: z.string().optional().describe("Parameter name (required for all actions except list)"),
      newName: z.string().optional().describe("New name for rename action"),
      formula: z.string().optional().describe("Formula expression for set_formula action"),
      type: z.string().optional().describe("Parameter type for add action (e.g. 'IFC_TYPE', 'IFC_LENGTH', 'IFC_TEXT')"),
    },
    async (args, extra) => {
      const params: any = {
        action: args.action,
        familyId: args.familyId,
      };
      if (args.name !== undefined) params.name = args.name;
      if (args.newName !== undefined) params.newName = args.newName;
      if (args.formula !== undefined) params.formula = args.formula;
      if (args.type !== undefined) params.type = args.type;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_family_parameters", params);
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
              text: `Manage family parameters failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
