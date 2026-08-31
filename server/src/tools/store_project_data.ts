import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { memoryOp, projectId, toProps } from "../memory/legacyBridge.js";

export function registerStoreProjectDataTool(server: McpServer) {
  server.tool(
    "store_project_data",
    "Store project-level information in the current Revit model. The data is written INTO the model " +
      "via Extensible Storage, so it travels with the file rather than living in a sidecar database " +
      "that desynchronises when the model is copied, renamed or rolled back. Read it back with " +
      "query_stored_data or project_memory_query.",
    {
      project_name: z.string().describe("The name of the Revit project"),
      project_path: z.string().optional().describe("File path to the project"),
      project_number: z.string().optional().describe("Project number or identifier"),
      project_address: z.string().optional().describe("Project address or location"),
      client_name: z.string().optional().describe("Client name"),
      project_status: z.string().optional().describe("Project status (e.g. Active, Completed, On Hold)"),
      author: z.string().optional().describe("Project author or creator"),
      metadata: z.record(z.string()).optional().describe("Additional project metadata as key-value pairs"),
    },
    async (args: any) => {
      try {
        const { project_name, metadata, ...rest } = args;
        const response = await memoryOp("write", {
          entities: [
            {
              id: projectId(project_name),
              kind: "project",
              name: project_name,
              props: { ...toProps(rest), ...toProps(metadata) },
            },
          ],
          relations: [],
        });
        return {
          content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text" as const,
              text:
                "store_project_data failed: " +
                (error instanceof Error ? error.message : String(error)) +
                "\n\nThis tool now writes into the open Revit model, so it needs a live connection " +
                "and an open document.",
            },
          ],
          isError: true as const,
        };
      }
    }
  );
}
