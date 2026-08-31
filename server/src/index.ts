#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { readFileSync } from "fs";
import { registerTools } from "./tools/register.js";

// Create server instance
// The version is READ from package.json rather than duplicated here. It was
// hardcoded, so bumping the package to 1.0.1 left the server still telling every
// client it was 1.0.0 - a version that is wrong in the one place a client can
// actually see it.
const pkg = JSON.parse(
  readFileSync(new URL("../package.json", import.meta.url), "utf8")
) as { version?: string };

const server = new McpServer({
  name: "mcp-server-for-revit",
  version: pkg.version ?? "0.0.0",
});

// Start server
async function main() {
  // Register tools
  await registerTools(server);

  // Connect to transport
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Server start success");
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
