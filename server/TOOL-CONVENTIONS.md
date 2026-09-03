# Tool conventions (server/src/tools)

Every tool file is paid for on every model request: the client sends the whole
tool catalogue (name + description + JSON schema) with each turn. These rules
keep that cost down and keep small models from misreading results.

## 1. Shape of a tool file

```ts
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { callRevit } from "../utils/reply.js";          // ok, fail, errorMessage, fromRevit, guarded also exist
import { Pt, Line, ElementId, ElementIds, RGB, Limit } from "../utils/schemas.js"; // import only what you use

export function registerCreateWallTool(server: McpServer) {   // keep the existing export name
  server.tool(
    "create_wall",                                              // keep the tool name
    "One or two sentences: what it does, key precondition, units, what it returns.",
    { /* zod shape — same property names, types, optionality, enums, defaults as before */ },
    async (args) => callRevit("create_wall", args)             // keep the Revit command name and the exact params object
  );
}
```

- `callRevit(command, params, label?)` sends the command, turns `{success:false}` /
  `{ok:false}` / `{Success:false}` replies and connection errors into an
  `isError` result, prunes nulls and empty strings, rounds floats, compacts the
  JSON, and truncates oversized arrays with a `_truncated` marker.
- Tools that post-process the reply use `withRevitConnection` + `ok(data)` /
  `fail(message, extra?)` from `../utils/reply.js`. Never return an error as a
  plain success text: every failure path must go through `fail()`.
- Tools that do not talk to Revit (Dynamo file tools, knowledge memory) use
  `ok()` / `fail()` the same way.

## 2. What must NOT change (the invariant checker enforces this)

- Tool name, register function name, Revit command name.
- The params object sent to Revit: same keys, same values, same defaults
  (`args.limit || 100` stays unless the file is listed in section 5).
- Input schema structure: property names, required vs optional, types, enums,
  `.default()` values, nested shapes. Only `description` text may change.
  Using `Pt` for `{x,y,z}` numbers or `ElementId` for `z.number().int()` is
  fine because the JSON schema is identical.

## 3. Descriptions

- Tool description: at most ~240 characters. Say what it does, the unit (mm
  unless the handler works differently), one precondition (e.g. "needs a plan
  view", "ids from get_current_view_elements"), and what comes back. No
  marketing ("intelligent", "powerful"), no worked examples, no repetition of
  parameter names that the schema already shows.
- Property descriptions: at most ~60 characters, and only when the name alone
  is ambiguous. `x`, `y`, `z`, `startPoint`, `elementId`, `name` need none.
  Enum values need no description when the enum is self-explanatory.
- State units once, in the tool description, not on every number.

## 4. Results

- Compact JSON via the helpers; never `JSON.stringify(x, null, 2)`.
- Keep results small: return ids and names, not the whole element, unless the
  tool exists to return details.
- A failure is `isError: true` with `{ok:false, error, ...hint}`.

## 5. Listing tools

`get_current_view_elements`, `get_selected_elements` and
`get_available_family_types` default their `limit` to 30 (was 100) and use
`Limit(30)` from schemas.ts. Every other default stays as it was.

## 6. Catalogue

Tool groups and the start-up profile live in `src/catalog.ts`. A new tool must
be added to a group there, or it lands in `other` and is only visible with
`REVIT_MCP_PROFILE=full`.
