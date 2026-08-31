import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExportViewsTool(server: McpServer) {
  server.tool(
    "export_views",
    "Export Revit views to various formats (PNG, JPG, DWG, DXF, IFC, DGN).",
    {
      data: z.array(z.object({
        viewIds: z.array(z.number()).describe("View IDs to export"),
        format: z.enum(["PNG", "JPG", "DWG", "DXF", "IFC", "DGN"]).describe("Export format"),
        folderPath: z.string().describe("Output folder path"),
        fileName: z.string().describe("Base file name"),
      })).describe("Array of export tasks"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_views", params);
        });
        return { content: [{ type: "text", text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text", text: `Export views failed: ${error instanceof Error ? error.message : String(error)}` }] };
      }
    }
  );
}
