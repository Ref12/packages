# Ref12.WasmNative

An MSBuild **project SDK** that makes `Microsoft.NET.Sdk.WebAssembly` projects do **native relink** and **AOT** without `dotnet workload install wasm-tools`.
The workload is only a set of NuGet packs plus a manifest that imports them as MSBuild SDKs. This SDK does the same at
*evaluation* time through the NuGet SDK resolver, so nothing is installed machine-wide and `dotnet workload list` stays empty.


## Use

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <Sdk Name="Ref12.WasmNative" Version="0.1.0-preview.1" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <WasmBuildNative>true</WasmBuildNative>            <!-- relink -->
    <!-- <RunAOTCompilation>true</RunAOTCompilation> --> <!-- or AOT -->
  </PropertyGroup>
</Project>
```

or leave the `<Sdk>` element out and put the version in `global.json`:

```json
{ "msbuild-sdks": { "Ref12.WasmNative": "0.1.0-preview.1" } }
```
(then still write `<Sdk Name="Ref12.WasmNative" />` in the csproj). Nothing happens unless a native feature is requested: a plain build downloads nothing extra.

Native features (same list the SDK's own workload check uses): `WasmBuildNative`, `RunAOTCompilation`, `WasmEnableSIMD=false`, `WasmEnableExceptionHandling=false`,
`InvariantTimezone`, `WasmNativeStrip=false`, `WasmNativeDebugSymbols`, `WasmSingleFileBundle=false`, `EnableDiagnostics`, `WasmProfilers`, `EmccInitialHeapSize`, `EmccMaximumHeapSize`.

Properties: `Ref12WasmNativeEnabled=false` (off switch), `Ref12WasmNativeAot=true` (AOT from the command line, see Limits),
`Ref12WasmNativeUntested=warn|error|ignore`, `Ref12WasmNativeVersion` (override the pack version), `Ref12WasmNativeHostRid`,
`Ref12WasmNativeManifestSource=nuget|sdk|table` (force one manifest source; default tries them in that order).

## How it works / no hard-coded versions

* **Pack versions**: `$(BundledNETCoreAppPackageVersion)`, i.e. the running SDK's bundled runtime version (= the runtime pack version the SDK uses).
* **Pack names, emscripten version, host variants**: read from the workload manifests of the running SDK band. The band is derived from `$(NETCoreSdkVersion)`
  (`10.0.401` -> `10.0.4xx`, `11.0.100-rc.1.x` -> `11.0.1xx-rc.1`); the manifests are fetched as the NuGet packages
  `Microsoft.NET.Workload.Mono.ToolChain.Current.Manifest-<band>` and `...Emscripten.Current.Manifest-<band>` (version = the manifest version the SDK itself has installed for that band)
  by importing them with the NuGet SDK resolver, then parsed with property functions. The Sdk-kind packs of the `Microsoft.NET.Runtime.*` / `...Runtime.AOT.Cross.<tfm>.browser-wasm` entries are resolved through their `alias-to` for the host RID, so Python / musl / arm64 variants fall out of the manifest.
  If NuGet is unreachable: the SDK's installed copy of the manifests, then a built-in table (`build/fallback/net<N>.json`, generated from the SDK; the last resort).
* **Per-major switches**: `build/net<N>.props` (before the SDK's manifests: keeps them from demanding uninstalled workload packs) and `build/net<N>.targets` (after: pretends the workload resolved). `build/Majors.props` names the newest major; an SDK major without files uses the newest.
* **Untested band**: a warning `R12WASM001` naming the band (`Ref12WasmNativeUntested=error` makes it `R12WASM002`). Tested bands are listed in `net<N>.props`.
* Runs at evaluation time, before restore: the SDK's `Sdk.props` runs first, the rest is hooked in through `CustomAfterMicrosoftCommonTargets`.

## What it downloads (Linux x64, .NET 10.0.401 -> 1.1 GB extracted NuGet cache; Windows roughly 1.6 GB)

| when | packs | extracted |
|---|---|---|
| any build | Mono runtime pack, `Microsoft.NET.Sdk.WebAssembly.Pack`, illink (the SDK needs these without us) | ~100 MB |
| relink / AOT | emscripten Sdk, Node, Cache (+ Python on win/osx), `Runtime.WebAssembly.Sdk`, `MonoTargets.Sdk`, `Runtime.AOT.<host>.Cross.browser-wasm`, the two workload manifests | ~1.0 GB |
| AOT only | `MonoAOTCompiler.Task` | small |

Relink needs the AOT cross compiler pack too (it generates the icall table). Sizes are from the CI proof runs, see the Actions summary.

## CI cache key

CI of this repo deliberately uses an empty cache. For your own CI, cache `~/.nuget/packages` (or `$NUGET_PACKAGES`) with a key such as

```
nuget-wasm-${{ runner.os }}-${{ env.SDK_VERSION }}-${{ hashFiles('global.json', '**/*.csproj') }}
```

where `SDK_VERSION` is the output of `dotnet --version`: the pack versions follow the SDK's bundled runtime version, so a new SDK means new packs (keep the SDK version in the key, not the package versions).

## Limits

* **Linux needs `python3`** on the machine: emscripten uses it and there is no Python pack for Linux. (An OS package, not a workload.)
* **Do not set `RunAOTCompilation=true` as a global property** (`-p:RunAOTCompilation=true`): the SDK's emscripten manifest honours global properties and demands the workload before any of our targets can act. Put it in the csproj, or use `-p:Ref12WasmNativeAot=true` on the command line.
* The Roslyn-sized apps take a long time to AOT (minutes); relink adds a few seconds.
* Only the `browser-wasm` + `Microsoft.NET.Sdk.WebAssembly` path is covered. Blazor WebAssembly SDK, wasi and mobile packs are not touched.
* Pack ids/versions on NuGet may stop matching a given preview SDK; then the band warning and a failing restore are the signal (the weekly CI run is there to see that early).

## Per-major support

| SDK | band tested in CI | status |
|---|---|---|
| 10.0.x | 10.0.4xx (Linux, Windows) | supported |
| 11.0 preview | 11.0.1xx-rc.1 (see CI) | tested on the newest preview only; other bands warn |
| 12+ | none | uses the net11 switches, warns `R12WASM001` |
| 9 and older | - | no per-major files; not supported |

## Get it

Not on nuget.org yet. Add the Ref12 feed, mapped to this id only (see https://github.com/Ref12/packages#consume):

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="ref12" value="https://github.com/Ref12/packages/releases/download/feed/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="ref12"><package pattern="Ref12.WasmNative" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

As an MSBuild SDK use the `<Sdk Name=... Version=.../>` element or `global.json` above. As a plain package (`PackageReference`) it does nothing: the SDK is resolved by the MSBuild SDK resolver, not by restore of the project.

## Source and licence

https://github.com/Ref12/packages/tree/main/wasm-native. MIT.
