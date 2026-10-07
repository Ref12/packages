#!/usr/bin/env bash
# Writes .github/workflows/ci.yml for when this folder becomes the ROOT of its own repo (paths lose the templates/wasm-native-sdk/ prefix).
# Policy on nuget.org then names that file: ci.yml.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p .github/workflows
sed -e 's#templates/wasm-native-sdk/##g' -e 's#^name: wasm-native-sdk#name: ci#' -e "s#paths: \[.*\]#paths-ignore: ['**.md']#" ../../.github/workflows/wasm-native-sdk.yml > .github/workflows/ci.yml
echo "wrote .github/workflows/ci.yml"
