import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateRampTool(server: McpServer) {
  server.tool(
    "create_ramp",
    "Ramp creation is not supported. The Revit API (2022-2027) exposes no public ramp-creation surface. Use the Revit UI to create ramps.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Ramp start point in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Ramp end point in mm"),
        width: z.number().describe("Ramp width in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        topLevel: z.number().describe("Top level elevation in mm"),
        baseOffset: z.number().optional().describe("Base offset in mm"),
        topOffset: z.number().optional().describe("Top offset in mm"),
        typeId: z.number().optional().describe("Ramp type ID"),
        rampType: z.string().optional().describe("Ramp type name"),
        material: z.string().optional().describe("Ramp material"),
        slope: z.number().optional().describe("Ramp slope in percent"),
      })).describe("Array of ramps to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_ramp", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create ramp failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
