import fs from "fs";
import os from "os";
import path from "path";

/**
 * Where this server is allowed to keep data that must outlive a single run.
 *
 * NOT beside the package. The published launch command is `npx -y
 * mcp-server-for-revit`, which resolves into an npm cache directory that npm is
 * free to re-resolve or clear at any time. Anything written relative to
 * `__dirname` therefore lives in disposable storage: a cache clear silently
 * destroys it, with no warning and no export path. That was a measured defect in
 * the 1.0.0 release, where the SQLite store landed inside
 * `...\npm-cache\_npx\<hash>\node_modules\mcp-server-for-revit\revit-data.db`.
 *
 * These are the conventional per-user data locations instead, and
 * REVIT_MCP_DATA_DIR overrides them for anyone who wants the store somewhere
 * specific (a synced folder, a project directory, a test sandbox).
 */
export function dataDir(): string {
  const override = process.env.REVIT_MCP_DATA_DIR;
  if (override && override.trim()) return path.resolve(override.trim());

  const app = "mcp-servers-for-revit";
  if (process.platform === "win32") {
    const base = process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming");
    return path.join(base, app);
  }
  if (process.platform === "darwin") {
    return path.join(os.homedir(), "Library", "Application Support", app);
  }
  const base = process.env.XDG_DATA_HOME || path.join(os.homedir(), ".local", "share");
  return path.join(base, app);
}

export function ensureDir(dir: string): string {
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

export function knowledgeDir(): string {
  return ensureDir(path.join(dataDir(), "knowledge"));
}
