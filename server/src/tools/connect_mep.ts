import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerConnectMepTool(server: McpServer) {
  server.tool(
    "connect_mep",
    "Connect MEP elements via their connectors. Element ids from get_current_view_elements. connectType is logged only; all types use the same ConnectTo call. Returns connection results.",
    {
      data: z.array(z.object({
        elementId1: z.number(),
        elementId2: z.number(),
        connectorIndex1: z.number().optional().describe("Connector index on element 1"),
        connectorIndex2: z.number().optional().describe("Connector index on element 2"),
        connectType: z.enum(["Direct", "Elbow", "Tee", "Reducer", "Cross"]).optional(),
      })),
    },
    async (args) => callRevit("connect_mep", args)
  );
}
