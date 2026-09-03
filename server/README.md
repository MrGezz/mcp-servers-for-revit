# mcp-server-for-revit

MCP server for interacting with Autodesk Revit through AI assistants like Claude.

This is the MCP server half of [mcp-servers-for-revit (MrGezz fork)](https://github.com/MrGezz/mcp-servers-for-revit). It exposes Revit operations as MCP tools and talks to the Revit add-in from the same repository over WebSocket.

> [!NOTE]
> The add-in must be installed and running inside Revit. Setup, the tool list, the tool groups and the `REVIT_MCP_*` environment variables are documented in the [main README](https://github.com/MrGezz/mcp-servers-for-revit#readme).
>
> This fork does not publish to npm. `npx -y mcp-server-for-revit` runs the package published by the [original project](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit), which is older and has far fewer tools. Use the copy the installer puts in `%AppData%\mcp-servers-for-revit\server\`, or the `-server.zip` attached to each [release](https://github.com/MrGezz/mcp-servers-for-revit/releases).

## Setup

**Claude Code**

```bash
claude mcp add --scope user mcp-server-for-revit -- node "C:\Users\<you>\AppData\Roaming\mcp-servers-for-revit\server\build\index.js"
```

**Claude Desktop** - Settings > Developer > Edit Config > `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "node",
            "args": ["C:\\Users\\<you>\\AppData\\Roaming\\mcp-servers-for-revit\\server\\build\\index.js"]
        }
    }
}
```

Restart the client. When its tool list shows `revit_tools` and `say_hello`, the server is connected; `revit_tools {action: "list"}` shows the groups that can be switched on.

## Development

```bash
npm install
npm run build
node build/utils/selfTest.js     # reply helpers and tool catalogue
node build/dynamo/selfTest.js    # Dynamo graph round-trip harness
```

Tool files follow [TOOL-CONVENTIONS.md](TOOL-CONVENTIONS.md).

## License

[MIT](https://github.com/MrGezz/mcp-servers-for-revit/blob/features/icz-addin/LICENSE)
