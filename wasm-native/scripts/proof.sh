#!/usr/bin/env bash
# Clean-cache proof (Linux/macOS): pack the SDK, then plain / relink / AOT publish of smoke/app with an EMPTY NuGet cache,
# 'dotnet workload list' must stay empty, optional browser smoke test.  Usage: proof.sh [tfm] [outdir]
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$(pwd); TFM="${1:-net10.0}"; RES="${2:-$ROOT/results}"; VER=0.0.1-ci
mkdir -p "$RES"; rm -rf artifacts out "$RES"/*; L="$RES/proof-$TFM.md"
log() { echo "$*" | tee -a "$L"; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export NUGET_PACKAGES="${PROOF_NUGET:-$(mktemp -d)/nuget}"
chk() { if dotnet workload list 2>&1 | grep -Eqi "wasm|emscripten|maui|android|ios"; then log "WORKLOAD PRESENT"; dotnet workload list | tee -a "$L"; exit 1; fi; }
if [ "$(uname)" = Linux ] && ! command -v python3 >/dev/null; then sudo apt-get install -y -qq python3 >/dev/null 2>&1 || apt-get install -y -qq python3 >/dev/null; fi
log "# Ref12.WasmNative proof, $TFM"; log "SDK: $(dotnet --version); host: $(uname -sm); python3: $(python3 --version 2>&1 || true)"
log '```'; dotnet workload list 2>&1 | tee -a "$L"; log '```'; chk
bash scripts/pack.sh $VER >/dev/null
# feed: only the packed SDK (read-only copy in a temp dir, so the cache below stays clean) + nuget.org
FEED=$(mktemp -d); cp artifacts/*.nupkg "$FEED"/
W=$(mktemp -d); cp -r smoke/. "$W"/; cd "$W"
cat > nuget.config <<EOF
<configuration><packageSources><clear/><add key="local" value="$FEED"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
EOF
echo "{\"msbuild-sdks\":{\"Ref12.WasmNative\":\"$VER\"}}" > global.json
cp "$ROOT/smoke/smoke.mjs" . ; rm -rf "$NUGET_PACKAGES"
log "NUGET_PACKAGES=$NUGET_PACKAGES (clean)"
FAIL=0
for v in "plain:" "relink:-p:WasmBuildNative=true" "aot:-p:Ref12WasmNativeAot=true"; do
  n=${v%%:*}; f=${v#*:}; rm -rf app/obj app/bin
  s=$(date +%s.%N)
  if ! dotnet publish app -c Release -p:SmokeTfm=$TFM -o out/$n $f >build-$n.log 2>&1; then log "- $n: PUBLISH FAILED"; grep -E "error|warning R12" build-$n.log | sort -u | head -15 | tee -a "$L"; FAIL=1; cp build-$n.log "$RES"/; continue; fi
  e=$(date +%s.%N); w=$(ls out/$n/wwwroot/_framework/dotnet.native.*.wasm | head -1)
  log "- $n: publish $(awk "BEGIN{printf \"%.1f\", $e-$s}") s, dotnet.native.wasm $(stat -c %s "$w" 2>/dev/null || stat -f %z "$w") bytes"
  grep -E "warning R12|Ref12.WasmNative:" build-$n.log | sort -u | sed 's/^ */  /' | tee -a "$L" || true
  chk
done
log "NuGet cache after: $(du -sh "$NUGET_PACKAGES" | cut -f1); packages: $(ls "$NUGET_PACKAGES" | tr '\n' ' ')"
log '```'; dotnet workload list 2>&1 | tee -a "$L"; log '```'; chk
if [ "${SMOKE:-1}" = 1 ]; then
  [ -d "$ROOT/smoke/node_modules" ] || (cd "$ROOT/smoke" && npm i --no-save --no-package-lock --no-audit --no-fund playwright-core@1.50.0 >/dev/null 2>&1)
  for n in plain relink aot; do [ -d out/$n ] || continue
    if (cd "$ROOT/smoke" && OUT="$W/out/$n" BROWSER_CHANNEL=${BROWSER_CHANNEL:-chrome} node smoke.mjs) | tee -a "$L" | tail -1 | grep -q "SMOKE OK"; then log "- smoke $n: OK"; else log "- smoke $n: FAIL"; FAIL=1; fi
  done
fi
exit $FAIL
