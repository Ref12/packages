#!/usr/bin/env bash
# pack the SDK nupkg into <repo>/templates/wasm-native-sdk/artifacts (no publishing)
set -euo pipefail
cd "$(dirname "$0")/.."
VER="${1:-0.1.0}"
rm -rf artifacts; dotnet pack src/Ref12.WasmNative -c Release -p:PackageVersion=$VER -o artifacts -v:q -nologo
ls -la artifacts
