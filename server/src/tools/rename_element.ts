import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { ElementId } from "../utils/schemas.js";

export function registerRenameElementTool(server: McpServer) {
  server.tool(
    "rename_element",
    "Rename a Revit element. Works on elements with an editable Name parameter, levels, grids, and element types. Returns success or an error.",
    {
      elementId: ElementId,
      newName: z.string().min(1),
    },
    async (args) => callRevit("rename_element", args)
  );
}
