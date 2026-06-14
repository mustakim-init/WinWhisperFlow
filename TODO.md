# TODO

## Goal: Set up cocoindex-io/cocoindex-code MCP server ("github.com/cocoindex-io/cocoindex-code" as server name) and demonstrate a tool.

### Plan steps
1. Verify whether `blackbox_mcp_settings.json` exists and find existing MCP settings patterns.
2. Install the `cocoindex-code` package (likely via `pipx` or `uv`).
3. Create/modify `blackbox_mcp_settings.json` to include server:
   - server name: `github.com/cocoindex-io/cocoindex-code`
   - command: run `ccc mcp` (from the installed `cocoindex-code` package)
4. Launch/use the MCP server via Blackbox MCP settings and confirm it responds.
5. Demonstrate server capability by calling the MCP tool `search` with a small query against the repo.
6. Document the exact commands run and where the settings live.

