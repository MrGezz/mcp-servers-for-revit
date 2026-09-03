import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt } from "../utils/schemas.js";

export function registerCreateReferencePlaneTool(server: McpServer) {
  server.tool(
    "create_reference_plane",
    "Create reference planes in Revit (mm). ByLine needs bubbleEnd+freeEnd; ByNormal needs origin+normal; ByPoints needs 3+ points. Returns created plane ids.",
    {
      data: z.array(z.object({
        creationMethod: z.enum(["ByLine", "ByNormal", "ByPoints"]).optional(),
        bubbleEnd: Pt.optional(),
        freeEnd: Pt.optional(),
        origin: Pt.optional(),
        normal: Pt.optional(),
        points: z.array(Pt).optional(),
        name: z.string().optional(),
      })),
    },
    async (args) => callRevit("create_reference_plane", args)
  );
}
