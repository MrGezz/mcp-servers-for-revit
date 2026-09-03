import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerExportViewsTool(server: McpServer) {
  server.tool(
    "export_views",
    "Exports Revit views to files (PNG/JPG/DWG/DXF/IFC/DGN). Each task specifies view ids, format, output folder, and base file name. Views must exist in the model. Returns export results.",
    {
      data: z.array(z.object({
        viewIds: z.array(z.number()),
        format: z.enum(["PNG", "JPG", "DWG", "DXF", "IFC", "DGN"]),
        folderPath: z.string(),
        fileName: z.string(),
      })),
    },
    async (args) => callRevit("export_views", args)
  );
}
