import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerManageFamilyParametersTool(server: McpServer) {
  server.tool(
    "manage_family_parameters",
    "Add, rename, remove, or set a formula for a parameter in the currently open family (.rfa). familyId only verifies a Family element exists in the active document; it does not select or open a different family. Returns success or failure.",
    {
      action: z.enum(["add", "rename", "remove", "set_formula"]),
      familyId: ElementId,
      name: z.string().optional(),
      newName: z.string().optional(),
      formula: z.string().optional(),
      type: z.string().optional().describe("ForgeTypeId spec string, e.g. 'autodesk.spec.aec:length-2.0.0', 'autodesk.spec:spec.string-2.0.0'. Omit to default to Number."),
    },
    async (args) => callRevit("manage_family_parameters", args)
  );
}
