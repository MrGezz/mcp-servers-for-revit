import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateMepSystemTool(server: McpServer) {
  server.tool(
    "create_mep_system",
    "Create MEP systems (mechanical or piping) and assign elements to them. Supports supply air, return air, exhaust air, sanitary, hydronic supply, and hydronic return.",
    {
      data: z.array(z.object({
        systemType: z.enum(["SupplyAir", "ReturnAir", "ExhaustAir", "Sanitary", "HydronicSupply", "HydronicReturn"]).describe("MEP system type"),
        name: z.string().describe("Name to assign to the system"),
        elementIds: z.array(z.number()).describe("Element IDs to assign to this system"),
      })).describe("Array of MEP system definitions to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_mep_system", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create MEP system failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
