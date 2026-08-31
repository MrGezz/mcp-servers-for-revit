import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateOpeningTool(server: McpServer) {
  server.tool(
    "create_opening",
    "Create openings in the Revit model. Supports wall openings, floor openings, roof openings, and shaft openings with host element, location, and dimensions. All units in mm.",
    {
      data: z.array(z.object({
        hostElementId: z.number().describe("Host element ID"),
        openingType: z.string().optional().describe("Opening type: Wall, Floor, Roof, or Shaft. (The C# enum members are named WallOpening/FloorOpening/RoofOpening/ShaftOpening, but each carries [EnumMember(Value = \"Wall\")] and the enum is serialised with StringEnumConverter, so these short names are the canonical wire values.)"),
        location: z.object({ x: z.number(), y: z.number(), z: z.number() }).describe("Opening location in mm"),
        width: z.number().describe("Opening width in mm"),
        height: z.number().describe("Opening height in mm"),
        baseLevel: z.number().optional().describe("Base level elevation in mm"),
        topLevel: z.number().optional().describe("Top level elevation in mm"),
      })).describe("Array of openings to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_opening", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Create opening failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
