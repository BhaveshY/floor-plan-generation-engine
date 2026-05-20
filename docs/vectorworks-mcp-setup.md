# Vectorworks MCP Setup

The Vectorworks MCP server exposes the floor plan engine through Model Context Protocol stdio tools. It is intentionally thin: it does not require the Vectorworks SDK and it does not mutate a Vectorworks document by itself. Instead, it gives MCP clients and Vectorworks-side adapters a stable way to generate or validate JSON, then import the output using the engine's layer names and `externalId` values.

## What It Provides

- `vectorworks_generate_floor_plan`: generate ranked variants from an `EngineInput` object or a bundled sample.
- `vectorworks_validate_floor_plan`: dry-run input validation before import or bake.
- `vectorworks_list_samples`: show bundled samples.
- `vectorworks_get_sample_input`: return a starter input JSON object.
- `vectorworks_get_contract_schema`: return the published input or output JSON Schema.
- `vectorworks_health`: confirm the MCP server can find bundled artifacts.

Generated outputs include `metadata.layers` values such as `FP::Generated::Units`, `FP::Generated::Walls`, and `FP::Generated::Diagnostics`. Generated objects also carry deterministic `fp://...` external ids, which adapters should store on Vectorworks objects so repeat imports can update existing geometry instead of duplicating it.

## Install On Windows

From the repo root:

```powershell
.\scripts\install-vectorworks-mcp.ps1
```

For a double-click setup path, run `scripts\install-vectorworks-mcp.bat`.

The installer:

- Uses `dotnet` if available, otherwise installs a local .NET 8 SDK under `.dotnet/`.
- Packs `FloorPlanGeneration.VectorworksMcp`.
- Installs `floorplan-vectorworks-mcp` as a local tool under `.tools/`.
- Runs `floorplan-vectorworks-mcp --self-test`.
- Writes `outputs/vectorworks-mcp-client-config.json`.

To also update a known client config:

```powershell
.\scripts\install-vectorworks-mcp.ps1 -Client ClaudeDesktop
.\scripts\install-vectorworks-mcp.ps1 -Client Cursor
.\scripts\install-vectorworks-mcp.ps1 -Client Custom -ConfigPath "$env:APPDATA\SomeClient\mcp.json"
```

Existing configs are backed up before the `mcpServers.vectorworks-floor-engine` entry is replaced.

## Install On macOS Or Linux

```bash
./scripts/install-vectorworks-mcp.sh
```

To merge into a specific MCP config file, install `jq` and pass:

```bash
./scripts/install-vectorworks-mcp.sh --config-path "$HOME/.config/some-client/mcp.json"
```

## Manual Client Config

If you prefer to configure your MCP client manually, use the generated snippet in `outputs/vectorworks-mcp-client-config.json`. It has this shape:

```json
{
  "mcpServers": {
    "vectorworks-floor-engine": {
      "command": "C:\\path\\to\\Floor Engine\\.tools\\floorplan-vectorworks-mcp.exe",
      "args": ["--stdio"],
      "env": {
        "FLOOR_ENGINE_REPO": "C:\\path\\to\\Floor Engine",
        "DOTNET_ROOT": "C:\\path\\to\\Floor Engine\\.dotnet"
      }
    }
  }
}
```

Restart the MCP client after changing its config.

## Smoke Test

Windows:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.dotnet).Path
@'
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1.0"}}}
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"vectorworks_health","arguments":{}}}
'@ | .\.tools\floorplan-vectorworks-mcp.exe --stdio
```

macOS/Linux:

```bash
export DOTNET_ROOT="$PWD/.dotnet"
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1.0"}}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"vectorworks_health","arguments":{}}}' \
  | ./.tools/floorplan-vectorworks-mcp --stdio
```

You should see JSON-RPC responses with `capabilities.tools` and a health result where `ok` is true.

## Adapter Notes

Vectorworks-side import should treat engine output as the source of truth:

- Create or reuse layers from `output.metadata.layers`.
- Bake units, rooms, corridors, walls, doors, labels, diagnostics, and topology from the selected variant.
- Store each element's `externalId` on the Vectorworks object.
- On re-import, update objects with matching `externalId` and remove stale objects for that variant only after the new output validates.
- Do not bake `status = failed` outputs as usable plans; display diagnostics grouped by severity, code, and source id.

For the geometry contract and layer details, see `docs/rhino-grasshopper-adapter-contract.md`; the same JSON contract applies to Vectorworks adapters.
