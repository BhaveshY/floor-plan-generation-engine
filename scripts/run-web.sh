#!/usr/bin/env bash
set -euo pipefail

PORT="${1:-5127}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOCAL_DOTNET="$REPO_ROOT/.dotnet/dotnet"

get_dotnet() {
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -Eq '^8\.'; then
    command -v dotnet
    return
  fi

  if [ -x "$LOCAL_DOTNET" ] && "$LOCAL_DOTNET" --list-sdks 2>/dev/null | grep -Eq '^8\.'; then
    printf '%s\n' "$LOCAL_DOTNET"
    return
  fi

  echo "Installing a local .NET 8 SDK for this folder..." >&2
  mkdir -p "$REPO_ROOT/.dotnet"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$REPO_ROOT/.dotnet/dotnet-install.sh"
  bash "$REPO_ROOT/.dotnet/dotnet-install.sh" --channel 8.0 --install-dir "$REPO_ROOT/.dotnet" --no-path
  printf '%s\n' "$LOCAL_DOTNET"
}

DOTNET_PATH="$(get_dotnet)"
URL="http://localhost:$PORT"
echo "Starting Floor Plan Engine Web at $URL"
"$DOTNET_PATH" run --project "$REPO_ROOT/FloorPlanGeneration.Web" --urls "$URL"
