import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { t, localeStatus } from "../i18n/index.js";

export async function registerTools(server: McpServer) {
  // OPTIONAL LOCALISATION, applied at the one place every tool passes through.
  //
  // Tool descriptions are what an AI client reads to choose a tool, so they are the
  // highest-value strings in the server. Rather than retrofit a t() call into ~90
  // files, server.tool is wrapped once here: the English text stays the source of
  // truth in each file, and a translation is substituted only when a catalogue is
  // configured (REVIT_MCP_LOCALE) and actually contains that string.
  //
  // With no locale set this is an identity function, so the default path is
  // unchanged.
  const locale = localeStatus();
  if (!locale.isDefault) {
    const original = server.tool.bind(server);
    (server as any).tool = (name: string, description: string, ...rest: any[]) =>
      typeof description === "string"
        ? (original as any)(name, t(description), ...rest)
        : (original as any)(name, description, ...rest);
    console.error(
      `[i18n] locale ${locale.active} active with ${locale.entries} entry/entries; ` +
        "tool descriptions will be translated where a translation exists."
    );
  }

  // Get the directory path of the current file
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  // Read all files in the tools directory
  const files = fs.readdirSync(__dirname);

  // Filter to .ts or .js files, excluding index and register files
  const toolFiles = files.filter(
    (file) =>
      (file.endsWith(".ts") || file.endsWith(".js")) &&
      file !== "index.ts" &&
      file !== "index.js" &&
      file !== "register.ts" &&
      file !== "register.js"
  );

  // Dynamically import and register each tool
  for (const file of toolFiles) {
    try {
      // Build the import path
      const importPath = `./${file.replace(/\.(ts|js)$/, ".js")}`;

      // Dynamically import the module
      const module = await import(importPath);

      // Find and execute the registration function
      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );

      if (registerFunctionName) {
        module[registerFunctionName](server);
        console.error(`Tool registered: ${file}`);
      } else {
        console.warn(`Warning: No registration function found in file ${file}`);
      }
    } catch (error) {
      console.error(`Error registering tool ${file}:`, error);
    }
  }
}
