#!/usr/bin/env bash
set -euo pipefail

SAMPLE="${1:-rectangular-core}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUTPUT="${2:-$REPO_ROOT/outputs/$SAMPLE-output.json}"
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
  bash "$REPO_ROOT/.dotnet/dotnet-install.sh" --channel 8.0 --install-dir "$REPO_ROOT/.dotnet" --no-path >&2
  if ! [ -x "$LOCAL_DOTNET" ] || ! "$LOCAL_DOTNET" --list-sdks 2>/dev/null | grep -Eq '^8\.'; then
    echo "Local .NET 8 SDK install did not produce a runnable dotnet binary at $LOCAL_DOTNET" >&2
    exit 1
  fi
  printf '%s\n' "$LOCAL_DOTNET"
}

mkdir -p "$(dirname "$OUTPUT")"
DOTNET_PATH="$(get_dotnet)"

echo "Running sample '$SAMPLE'..."
"$DOTNET_PATH" run --project "$REPO_ROOT/FloorPlanGeneration.Cli" -- --sample "$SAMPLE" --output "$OUTPUT" --summary
echo "Done. Output written to $OUTPUT"
