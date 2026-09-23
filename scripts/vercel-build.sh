#!/usr/bin/env bash
# Builds the site on Vercel (or any Linux/macOS machine).
# Vercel's build machines have Node but not .NET, which Fable needs to turn the
# F# source into JavaScript, so this installs .NET into ./.dotnet first.
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_ROOT="$PWD/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
# Build machines may lack the ICU library; the compiler doesn't need it.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^10\.'; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT"
fi

dotnet tool restore
dotnet fable src
npx vite build
