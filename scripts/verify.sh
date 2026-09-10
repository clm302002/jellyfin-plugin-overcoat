#!/usr/bin/env bash
# Canonical deterministic verification for Overcoat.
#
# Builds the plugin against the pinned Jellyfin server family and runs the unit
# suite. Both must be green before any work is called complete.
set -euo pipefail

cd "$(dirname "$0")/.."

PLUGIN=Jellyfin.Plugin.Overcoat/Jellyfin.Plugin.Overcoat.csproj
TESTS=tests/Jellyfin.Plugin.Overcoat.Tests/Jellyfin.Plugin.Overcoat.Tests.csproj

echo "== dotnet SDKs =="
dotnet --list-sdks

echo
echo "== build (Release) =="
dotnet build "$PLUGIN" -c Release --nologo

echo
echo "== unit tests =="
dotnet test "$TESTS" -c Release --nologo

echo
echo "Verification passed."
