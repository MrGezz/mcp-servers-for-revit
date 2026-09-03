import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateTextNoteTool(server: McpServer) {
  server.tool(
    "create_text_note",
    "Create text note annotations in the active Revit view. Coordinates in mm. Returns the new element ids.",
    {
      data: z.array(
        z.object({
          location: Pt,
          text: z.string(),
          rotation: z.number().optional().default(0).describe("degrees"),
          width: z.number().optional().default(0).describe("mm; 0 = no limit"),
          textNoteTypeId: z.number().optional().default(-1).describe("-1 = default type"),
          viewId: z.number().optional().default(-1).describe("-1 = active view"),
          horizontalAlign: z.number().optional().default(0).describe("0=Left 1=Center 2=Right"),
          verticalAlign: z.number().optional().default(0).describe("0=Top 1=Middle 2=Bottom"),
        })
      ),
    },
    async (args) => callRevit("create_text_note", args)
  );
}
