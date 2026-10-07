# wasm-native

Source of **Ref12.WasmNative** (the package readme is `Ref12.WasmNative/README.md`).

| Path | What |
|---|---|
| `Ref12.WasmNative/` | the package project (`dotnet pack`; this is the `project` input of publish.yml) |
| `smoke/` | smoke app (`app/`) and the browser test (`smoke.mjs`) |
| `scripts/` | `pack.sh`, `proof.sh`, `proof.ps1` |

```
bash wasm-native/scripts/pack.sh 0.0.1-ci            # nupkg in wasm-native/artifacts
bash wasm-native/scripts/proof.sh net10.0            # clean cache: plain/relink/AOT + workload list + browser smoke
pwsh wasm-native/scripts/proof.ps1 -Channel 11.0 -Quality preview -Tfm net11.0
```

CI: `.github/workflows/wasm-native.yml`. Publish:

```
gh workflow run publish.yml -R ref12labs/packages -f repo=ref12labs/packages -f ref=main -f project=wasm-native/Ref12.WasmNative
```
