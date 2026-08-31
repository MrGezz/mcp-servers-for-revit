import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateMEPCurveTool(server: McpServer) {
  server.tool(
    "create_mep_curve",
    "Create MEP curves (duct, pipe, or conduit) in Revit. All units in mm.",
    {
      mepType: z.enum(["duct", "pipe", "conduit"]).describe("MEP element type"),
      start: z.object({
        x: z.number().describe("Start X in mm"),
        y: z.number().describe("Start Y in mm"),
        z: z.number().describe("Start Z in mm"),
      }).describe("Start point in mm"),
      end: z.object({
        x: z.number().describe("End X in mm"),
        y: z.number().describe("End Y in mm"),
        z: z.number().describe("End Z in mm"),
      }).describe("End point in mm"),
      level: z.number().describe("Level elevation in mm"),
      diameter: z.number().optional().default(200).describe("Diameter in mm"),
      systemType: z.string().optional().describe("System type name"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_mep_curve", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create MEP curve failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
