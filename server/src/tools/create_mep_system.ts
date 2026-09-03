import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerCreateMepSystemTool(server: McpServer) {
  server.tool(
    "create_mep_system",
    "Create named MEP systems and assign elements to them. Supported types: SupplyAir, ReturnAir, ExhaustAir, Sanitary, HydronicSupply, HydronicReturn. Returns created system element ids.",
    {
      data: z.array(z.object({
        systemType: z.enum(["SupplyAir", "ReturnAir", "ExhaustAir", "Sanitary", "HydronicSupply", "HydronicReturn"]),
        name: z.string(),
        elementIds: z.array(z.number()).describe("Element ids to assign to this system"),
      })),
    },
    async (args) => callRevit("create_mep_system", args)
  );
}
