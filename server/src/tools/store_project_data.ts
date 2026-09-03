import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { memoryOp, projectId, toProps } from "../memory/legacyBridge.js";
import { fromRevit, fail, errorMessage } from "../utils/reply.js";

export function registerStoreProjectDataTool(server: McpServer) {
  server.tool(
    "store_project_data",
    "Writes project metadata into the model via Extensible Storage; travels with the file. Read back with query_stored_data or project_memory_query.",
    {
      project_name: z.string(),
      project_path: z.string().optional(),
      project_number: z.string().optional(),
      project_address: z.string().optional(),
      client_name: z.string().optional(),
      project_status: z.string().optional().describe("e.g. Active, Completed, On Hold"),
      author: z.string().optional(),
      metadata: z.record(z.string()).optional().describe("Extra key-value pairs"),
    },
    async (args) => {
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
        return fromRevit(response);
      } catch (error) {
        return fail(
          `store_project_data failed: ${errorMessage(error)}` +
            "\n\nThis tool writes into the open Revit model — needs a live connection and an open document."
        );
      }
    }
  );
}
