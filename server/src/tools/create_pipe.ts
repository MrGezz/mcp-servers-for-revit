import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreatePipeTool(server: McpServer) {
  server.tool(
    "create_pipe",
    "Create pipes in the Revit model. Supports pipes with start/end points, diameter, system type, and level. All units in mm.",
    {
      data: z.array(z.object({
        startPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Start point in mm"),
        endPoint: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("End point in mm"),
        diameter: z.number().describe("Pipe diameter in mm"),
        baseLevel: z.number().describe("Base level elevation in mm"),
        baseOffset: z.number().optional().describe("Base offset in mm"),
        systemType: z.string().optional().describe("System type (Domestic Cold Water, Sanitary, etc.)"),
        pipeType: z.string().optional().describe("Pipe type name"),
        typeId: z.number().optional().describe("Pipe type ID"),
      })).describe("Array of pipes to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_pipe", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create pipe failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
