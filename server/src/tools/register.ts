import type { McpServer, RegisteredTool } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ZodFirstPartyTypeKind, ZodType, type ZodTypeAny } from "zod";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { t, localeStatus } from "../i18n/index.js";
import { applyProfile, groupOf, registerRevitToolsTool, type Registry } from "../catalog.js";

/**
 * Deep-clone a zod schema so that no two nodes share identity.
 *
 * WHY. Shared fragments (Pt, ElementId, ...) are one object instance used in
 * many places. zod-to-json-schema, which the SDK runs at tools/list time,
 * replaces the second and later occurrences of an instance with a JSON-pointer
 * `$ref` ("#/properties/data/items/properties/startPoint"). That is valid JSON
 * Schema but not every MCP client or model API accepts it — VS Code already
 * needed a schema fix once (upstream #9). Cloning at registration keeps the
 * emitted schema fully inline, exactly as it was when every file spelled the
 * fragment out by hand.
 */
function fresh<T extends ZodTypeAny>(schema: T): T {
  const def: any = schema._def;
  const ctor: any = schema.constructor;
  const K = ZodFirstPartyTypeKind;
  switch (def.typeName) {
    case K.ZodObject: {
      const shape = def.shape();
      const next: Record<string, ZodTypeAny> = {};
      for (const key of Object.keys(shape)) next[key] = fresh(shape[key]);
      return new ctor({ ...def, shape: () => next });
    }
    case K.ZodArray:
      return new ctor({ ...def, type: fresh(def.type) });
    case K.ZodOptional:
    case K.ZodNullable:
    case K.ZodDefault:
    case K.ZodCatch:
    case K.ZodBranded:
    case K.ZodReadonly:
      return new ctor({ ...def, innerType: fresh(def.innerType) });
    case K.ZodEffects:
      return new ctor({ ...def, schema: fresh(def.schema) });
    case K.ZodUnion:
      return new ctor({ ...def, options: def.options.map((o: ZodTypeAny) => fresh(o)) });
    case K.ZodIntersection:
      return new ctor({ ...def, left: fresh(def.left), right: fresh(def.right) });
    case K.ZodTuple:
      return new ctor({ ...def, items: def.items.map((o: ZodTypeAny) => fresh(o)), rest: def.rest ? fresh(def.rest) : def.rest });
    case K.ZodRecord:
      return new ctor({ ...def, keyType: fresh(def.keyType), valueType: fresh(def.valueType) });
    case K.ZodMap:
      return new ctor({ ...def, keyType: fresh(def.keyType), valueType: fresh(def.valueType) });
    case K.ZodSet:
      return new ctor({ ...def, valueType: fresh(def.valueType) });
    case K.ZodPipeline:
      return new ctor({ ...def, in: fresh(def.in), out: fresh(def.out) });
    case K.ZodDiscriminatedUnion:
    case K.ZodLazy:
      return schema; // rare here; left as is rather than rebuilt incorrectly
    default:
      return new ctor({ ...def }); // leaf: number, string, boolean, enum, literal, ...
  }
}

function isShape(value: unknown): value is Record<string, ZodTypeAny> {
  return (
    typeof value === "object" &&
    value !== null &&
    !(value instanceof ZodType) &&
    Object.values(value as Record<string, unknown>).every((v) => v instanceof ZodType)
  );
}

/**
 * Register every tool module in this directory, then enable only the start-up
 * profile (see catalog.ts for why).
 *
 * `server.tool` is wrapped ONCE here rather than touched in ~90 files: the
 * wrapper applies the optional locale catalogue to the description and records
 * the RegisteredTool handle so the profile and `revit_tools` can switch tools
 * on and off later.
 */
export async function registerTools(server: McpServer): Promise<Registry> {
  const registry: Registry = new Map();
  const locale = localeStatus();
  if (!locale.isDefault) {
    console.error(
      `[i18n] locale ${locale.active} active with ${locale.entries} entry/entries; ` +
        "tool descriptions will be translated where a translation exists."
    );
  }

  const original = server.tool.bind(server);
  (server as any).tool = (name: string, ...rest: any[]) => {
    let description = "";
    if (typeof rest[0] === "string") {
      description = locale.isDefault ? rest[0] : t(rest[0]);
      rest[0] = description;
    }
    for (let i = 0; i < rest.length; i++) {
      if (isShape(rest[i])) {
        const cloned: Record<string, ZodTypeAny> = {};
        for (const key of Object.keys(rest[i])) cloned[key] = fresh(rest[i][key]);
        rest[i] = cloned;
      }
    }
    const registered: RegisteredTool = (original as any)(name, ...rest);
    registry.set(name, { tool: registered, group: groupOf(name), description });
    return registered;
  };

  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);
  const toolFiles = fs
    .readdirSync(__dirname)
    .filter((file) => /\.(ts|js)$/.test(file) && !/^(index|register)\.(ts|js)$/.test(file))
    .sort();

  const failures: string[] = [];
  for (const file of toolFiles) {
    try {
      const module = await import(`./${file.replace(/\.(ts|js)$/, ".js")}`);
      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );
      if (registerFunctionName) module[registerFunctionName](server);
      else failures.push(`${file}: no register function`);
    } catch (error) {
      failures.push(`${file}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  registerRevitToolsTool(server, registry);
  const { enabled, disabled } = applyProfile(registry);

  const unknownGroup = [...registry].filter(([, e]) => e.group === "other").map(([n]) => n);
  if (unknownGroup.length) console.error(`[catalog] tools without a group (add them to catalog.ts): ${unknownGroup.join(", ")}`);
  for (const f of failures) console.error(`[register] ${f}`);
  console.error(`[register] ${registry.size} tools registered from ${toolFiles.length} files; ${enabled.length} enabled, ${disabled.length} on demand via revit_tools`);
  return registry;
}
