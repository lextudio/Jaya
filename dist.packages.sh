#!/usr/bin/env bash
set -euo pipefail

# dist.packages.sh - convenience script to run the Cake build and produce packages
# Usage: ./dist.packages.sh [os] [version-prefix]
# Examples:
#   ./dist.packages.sh          # builds all targets (Deploy)
#   ./dist.packages.sh mac      # builds only mac targets
#   ./dist.packages.sh win 1.2.3

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"
CAKE_SCRIPT="$REPO_ROOT/build/build.cake"

OS_ARG="all"
VERSION_PREFIX="0.0.0"

if [ $# -ge 1 ] && [ -n "$1" ]; then
  OS_ARG="$1"
fi
if [ $# -ge 2 ] && [ -n "$2" ]; then
  VERSION_PREFIX="$2"
fi

echo "Running Cake build (script: $CAKE_SCRIPT) with OS=$OS_ARG VersionPrefix=$VERSION_PREFIX"

cd "$REPO_ROOT"

# Ensure dotnet is available
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet CLI not found in PATH. Please install .NET SDK (>= 6.0) or ensure 'dotnet' is on PATH." >&2
  exit 1
fi

# Ensure Cake.Tool is available; prefer dotnet-cake global tool if installed
if dotnet tool list -g | grep -q "cake.tool"; then
  CAKE_CMD=(dotnet-cake)
elif command -v dotnet-cake >/dev/null 2>&1; then
  CAKE_CMD=(dotnet-cake)
elif command -v cake >/dev/null 2>&1; then
  CAKE_CMD=(cake)
else
  # Use dotnet tool restore in repo-local tools manifest if available, otherwise use dotnet tool install
  echo "Installing Cake.Tool as a local dotnet tool (temporary)..."
  dotnet tool install --tool-path ./.tools Cake.Tool --version 1.3.0 || true
  CAKE_CMD=(./.tools/cake)
fi

CAKE_ARGS=(--verbosity Verbose --version-prefix "$VERSION_PREFIX" --OS "$OS_ARG")

echo "Invoking: ${CAKE_CMD[*]} $CAKE_SCRIPT ${CAKE_ARGS[*]}"
"${CAKE_CMD[@]}" "$CAKE_SCRIPT" "${CAKE_ARGS[@]}"

echo "Build finished. Output should be under ./publish/"
