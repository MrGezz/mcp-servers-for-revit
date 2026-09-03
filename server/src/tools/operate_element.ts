import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";

const ACTIONS = ["Select", "SelectionBox", "SetColor", "SetTransparency", "Delete", "Hide", "TempHide", "Isolate", "Unhide", "ResetIsolate"] as const;

export function registerOperateElementTool(server: McpServer) {
  server.tool(
    "operate_element",
    "Apply one action to elements in the active view. SetColor uses colorValue (RGB), SetTransparency uses transparencyValue (0-100); Hide/TempHide/Isolate/Unhide/ResetIsolate change view visibility; Delete removes the elements.",
    {
      data: z.object({
        elementIds: z.array(z.number()).min(1),
        action: z.enum(ACTIONS),
        transparencyValue: z.number().default(50).describe("0-100, for SetTransparency"),
        colorValue: z.array(z.number()).default([255, 0, 0]).describe("[r,g,b] 0-255, for SetColor"),
      }),
    },
    async (args) => callRevit("operate_element", args)
  );
}
