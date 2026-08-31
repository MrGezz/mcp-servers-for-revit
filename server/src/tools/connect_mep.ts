import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerConnectMepTool(server: McpServer) {
  server.tool(
    "connect_mep",
    "Connect two MEP elements via their connectors. Supports direct, elbow, tee, reducer, and cross connections.",
    {
      data: z.array(z.object({
        elementId1: z.number().describe("First element ID"),
        elementId2: z.number().describe("Second element ID"),
        connectorIndex1: z.number().optional().describe("Connector index on the first element"),
        connectorIndex2: z.number().optional().describe("Connector index on the second element"),
        connectType: z.enum(["Direct", "Elbow", "Tee", "Reducer", "Cross"]).optional().describe("Connection type"),
      })).describe("Array of connections to make"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("connect_mep", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Connect MEP failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
