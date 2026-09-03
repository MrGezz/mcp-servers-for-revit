import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerLoadFamilyTool(server: McpServer) {
  server.tool(
    "load_family",
    "Load a .rfa family file into the active Revit project. Returns a success flag and a message string.",
    {
      filePath: z.string().describe("Full path to the .rfa file"),
      familyName: z.string().optional(),
    },
    async (args) => callRevit("load_family", args)
  );
}
