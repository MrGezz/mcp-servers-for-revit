import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { errorMessage, fail, ok } from "../utils/reply.js";

export function registerSendCodeToRevitTool(server: McpServer) {
  server.tool(
    "send_code_to_revit",
    "Compile and run a C# snippet inside Revit: the body of `static object Execute(Document document, object[] parameters)` (System, System.Linq, System.Collections.Generic, Autodesk.Revit.DB/UI imported). Must `return` a JSON-serialisable value. Revit API units here are FEET. transactionMode 'auto' wraps it in one transaction; 'none' for read-only or self-managed transactions.",
    {
      code: z.string().describe("C# statements; must end with a return"),
      parameters: z.array(z.string()).optional().describe("Strings available as parameters[i]"),
      transactionMode: z.enum(["auto", "none"]).optional(),
    },
    async (args) => {
      try {
        const response = (await withRevitConnection(async (client) =>
          client.sendCommand("send_code_to_revit", {
            code: args.code,
            parameters: args.parameters ?? [],
            transactionMode: args.transactionMode ?? "auto",
          })
        )) as { success?: boolean; result?: string; errorMessage?: string };

        if (!response?.success) return fail(response?.errorMessage || "Code execution failed");

        // The handler JSON-serialises the snippet's return value into a STRING;
        // hand the value back as data so the model does not read escaped JSON.
        let result: unknown = response.result;
        if (typeof result === "string") {
          try {
            result = JSON.parse(result);
          } catch {
            /* plain text result, keep as is */
          }
        }
        return ok({ ok: true, result });
      } catch (error) {
        return fail(`send_code_to_revit failed: ${errorMessage(error)}`);
      }
    }
  );
}
