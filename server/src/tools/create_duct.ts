import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateDuctTool(server: McpServer) {
  server.tool(
    "create_duct",
    "Create ducts in the Revit model. Supports rectangular and round ducts with start/end points, width, height, system type, and level. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Start point in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("End point in mm"),
        width: z.number().describe("Duct width/diameter in mm"),
        height: z.number().optional().describe("Duct height in mm (rectangular ducts only)"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        baseOffset: z.number().optional().describe("Base offset in mm"),
        systemType: z.string().optional().describe("System type (Supply Air, Return Air, Exhaust Air)"),
        ductType: z.string().optional().describe("Duct type name"),
        typeId: z.number().optional().describe("Duct type ID"),
      })).describe("Array of ducts to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_duct", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create duct failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
