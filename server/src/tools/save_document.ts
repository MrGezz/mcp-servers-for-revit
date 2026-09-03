import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

export function registerSaveDocumentTool(server: McpServer) {
  server.tool(
    "save_document",
    "Saves the active Revit document to disk. No parameters required. Returns success or an error if the document cannot be saved.",
    {},
    async () => callRevit("save_document", {})
  );
}
