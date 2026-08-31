import { withRevitConnection } from "../utils/ConnectionManager.js";

/**
 * Bridge from the original flat store_project_data / store_room_data /
 * query_stored_data tools onto the model-scoped memory graph.
 *
 * The three tool NAMES are kept, because clients and prompts already refer to
 * them. What changed is where the data goes: into the Revit model via Extensible
 * Storage, rather than into a SQLite file resolved relative to the package
 * directory - which, under the documented `npx -y` launch command, sits inside the
 * npm cache and can be cleared without warning.
 *
 * Mapping:
 *   a project -> one entity, kind "project", id "project:<name>"
 *   a room    -> one entity, kind "room",    id "room:<roomId>"
 *   ownership -> one relation, kind "contains", project -> room
 */

export async function memoryOp(action: string, payload: unknown): Promise<any> {
  return withRevitConnection(async (revitClient) =>
    revitClient.sendCommand("project_memory_op", { action, payload })
  );
}

export const projectId = (name: string) => `project:${name}`;
export const roomId = (id: string) => `room:${id}`;

/** Drop undefined values and stringify the rest: props are a string map. */
export function toProps(source: Record<string, unknown> | undefined): Record<string, string> {
  const out: Record<string, string> = {};
  if (!source) return out;
  for (const [k, v] of Object.entries(source)) {
    if (v === undefined || v === null || v === "") continue;
    out[k] = typeof v === "string" ? v : String(v);
  }
  return out;
}
