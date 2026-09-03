import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";
import { Pt, ElementId } from "../utils/schemas.js";

export function registerSetElementCurveTool(server: McpServer) {
  server.tool(
    "set_element_curve",
    "Move the location curve of a linear element (wall, beam, pipe, duct) by replacing its start and end points (mm). Requires a model element with a linear location curve.",
    {
      elementId: ElementId,
      startPoint: Pt,
      endPoint: Pt,
    },
    async (args) => callRevit("set_element_curve", args)
  );
}
