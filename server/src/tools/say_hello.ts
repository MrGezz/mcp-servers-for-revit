import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSayHelloTool(server: McpServer) {
  server.tool(
    "say_hello",
    "Test the connection to Revit. By default this does NOT open a dialog: it returns the Revit " +
      "version and the open document's title, which proves the bridge reached a live session " +
      "without needing anyone in front of the screen. Set showDialog to open a dialog as well - " +
      "note that a modal dialog blocks every other command until it is dismissed.",
    {
      message: z
        .string()
        .optional()
        .describe("Message to show when showDialog is true. Defaults to 'Hello MCP!'"),
      showDialog: z
        .boolean()
        .optional()
        .describe(
          "Open a modal dialog in Revit (default false). While it is open, the shared " +
            "ExternalEvent queue is blocked and no other command can run."
        ),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("say_hello", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Say hello failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
