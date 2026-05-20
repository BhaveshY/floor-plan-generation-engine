#!/usr/bin/env bash
set -euo pipefail

SERVER_NAME="vectorworks-floor-engine"
GLOBAL_TOOL=0
CONFIG_PATH=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --global)
      GLOBAL_TOOL=1
      shift
      ;;
    --server-name)
      SERVER_NAME="${2:?Missing value for --server-name}"
      shift 2
      ;;
    --config-path)
      CONFIG_PATH="${2:?Missing value for --config-path}"
      shift 2
      ;;
    -h|--help)
      echo "Usage: scripts/install-vectorworks-mcp.sh [--global] [--server-name name] [--config-path path]"
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 64
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOCAL_DOTNET="$REPO_ROOT/.dotnet/dotnet"
PACKAGE_DIR="$REPO_ROOT/artifacts/packages"
TOOL_DIR="$REPO_ROOT/.tools"
SNIPPET_PATH="$REPO_ROOT/outputs/vectorworks-mcp-client-config.json"

get_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return
  fi

  if [ -x "$LOCAL_DOTNET" ]; then
    printf '%s\n' "$LOCAL_DOTNET"
    return
  fi

  echo "Installing a local .NET 8 SDK for this folder..." >&2
  mkdir -p "$REPO_ROOT/.dotnet"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$REPO_ROOT/.dotnet/dotnet-install.sh"
  bash "$REPO_ROOT/.dotnet/dotnet-install.sh" --channel 8.0 --install-dir "$REPO_ROOT/.dotnet" --no-path
  printf '%s\n' "$LOCAL_DOTNET"
}

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

DOTNET_PATH="$(get_dotnet)"
DOTNET_ROOT_VALUE="$(cd "$(dirname "$DOTNET_PATH")" && pwd)"
mkdir -p "$PACKAGE_DIR" "$(dirname "$SNIPPET_PATH")"

echo "Packing FloorPlanGeneration.VectorworksMcp..."
"$DOTNET_PATH" pack "$REPO_ROOT/FloorPlanGeneration.VectorworksMcp" -c Release -o "$PACKAGE_DIR"

if [ "$GLOBAL_TOOL" -eq 1 ]; then
  echo "Installing/updating global MCP tool..."
  if ! "$DOTNET_PATH" tool update FloorPlanGeneration.VectorworksMcp --global --add-source "$PACKAGE_DIR" --version 0.1.0; then
    "$DOTNET_PATH" tool install FloorPlanGeneration.VectorworksMcp --global --add-source "$PACKAGE_DIR" --version 0.1.0
  fi

  if [ -x "$HOME/.dotnet/tools/floorplan-vectorworks-mcp" ]; then
    COMMAND="$HOME/.dotnet/tools/floorplan-vectorworks-mcp"
  else
    COMMAND="floorplan-vectorworks-mcp"
  fi

  SELF_TEST_COMMAND="$COMMAND"
else
  mkdir -p "$TOOL_DIR"
  if [ -x "$TOOL_DIR/floorplan-vectorworks-mcp" ]; then
    echo "Updating local MCP tool in $TOOL_DIR..."
    "$DOTNET_PATH" tool update FloorPlanGeneration.VectorworksMcp --tool-path "$TOOL_DIR" --add-source "$PACKAGE_DIR" --version 0.1.0
  else
    echo "Installing local MCP tool in $TOOL_DIR..."
    "$DOTNET_PATH" tool install FloorPlanGeneration.VectorworksMcp --tool-path "$TOOL_DIR" --add-source "$PACKAGE_DIR" --version 0.1.0
  fi

  COMMAND="$TOOL_DIR/floorplan-vectorworks-mcp"
  SELF_TEST_COMMAND="$COMMAND"
fi

echo "Running MCP self-test..."
DOTNET_ROOT="$DOTNET_ROOT_VALUE" "$SELF_TEST_COMMAND" --self-test

COMMAND_ESCAPED="$(json_escape "$COMMAND")"
REPO_ROOT_ESCAPED="$(json_escape "$REPO_ROOT")"
DOTNET_ROOT_ESCAPED="$(json_escape "$DOTNET_ROOT_VALUE")"
SERVER_NAME_ESCAPED="$(json_escape "$SERVER_NAME")"

cat > "$SNIPPET_PATH" <<JSON
{
  "mcpServers": {
    "$SERVER_NAME_ESCAPED": {
      "command": "$COMMAND_ESCAPED",
      "args": ["--stdio"],
      "env": {
        "FLOOR_ENGINE_REPO": "$REPO_ROOT_ESCAPED",
        "DOTNET_ROOT": "$DOTNET_ROOT_ESCAPED"
      }
    }
  }
}
JSON

echo "Wrote MCP client config snippet to $SNIPPET_PATH"

if [ -n "$CONFIG_PATH" ]; then
  if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required to merge --config-path automatically. Install jq or merge the snippet manually." >&2
    exit 2
  fi

  mkdir -p "$(dirname "$CONFIG_PATH")"
  if [ -f "$CONFIG_PATH" ]; then
    cp "$CONFIG_PATH" "$CONFIG_PATH.bak-$(date +%Y%m%d%H%M%S)"
  else
    printf '{}\n' > "$CONFIG_PATH"
  fi

  TMP_CONFIG="$(mktemp)"
  jq \
    --arg name "$SERVER_NAME" \
    --arg command "$COMMAND" \
    --arg repo "$REPO_ROOT" \
    --arg dotnetRoot "$DOTNET_ROOT_VALUE" \
    '.mcpServers = (.mcpServers // {}) | .mcpServers[$name] = {command: $command, args: ["--stdio"], env: {FLOOR_ENGINE_REPO: $repo, DOTNET_ROOT: $dotnetRoot}}' \
    "$CONFIG_PATH" > "$TMP_CONFIG"
  mv "$TMP_CONFIG" "$CONFIG_PATH"
  echo "Configured MCP server '$SERVER_NAME' in $CONFIG_PATH"
fi

echo "Done. MCP command: $COMMAND --stdio"
