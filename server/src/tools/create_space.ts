import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateSpaceTool(server: McpServer) {
  server.tool(
    "create_space",
    "Create spaces in the Revit model for MEP analysis. All units in mm.",
    {
      data: z.array(z.object({
        name: z.string().describe("Space name"),
        number: z.string().describe("Space number"),
        location: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Insertion point in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        spaceType: z.string().optional().describe("Space type"),
      })).describe("Array of spaces to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_space", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create space failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
