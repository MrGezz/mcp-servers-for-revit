import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerSayHelloTool(server: McpServer) {
  server.tool(
    "say_hello",
    "Connection test: returns the Revit version and the open document's title without touching the model. showDialog opens a modal dialog in debug builds of the add-in only; release builds ignore it.",
    {
      message: z.string().optional().describe("Dialog text (debug builds only)"),
      showDialog: z.boolean().optional().describe("Debug builds only; blocks other commands while open"),
    },
    async (args) => callRevit("say_hello", args)
  );
}
