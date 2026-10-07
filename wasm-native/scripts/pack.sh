#!/usr/bin/env bash
# pack the SDK nupkg into wasm-native/artifacts (no publishing)
set -euo pipefail
cd "$(dirname "$0")/.."
VER="${1:-0.1.0-preview.2}"  # default = the version publish.yml builds
rm -rf artifacts; dotnet pack Ref12.WasmNative -c Release -p:PackageVersion=$VER -o artifacts -v:q -nologo
ls -la artifacts
