# Clean-cache proof on Windows (isolated SDK install, fresh NuGet cache). A machine-wide workload (Visual Studio) may be registered; every relink/AOT publish must still use the packs and emcc of the NuGet cache.
# Usage: pwsh scripts/proof.ps1 -Channel 10.0 [-Quality preview] -Tfm net10.0
param([string]$Channel='10.0',[string]$Quality='',[string]$Tfm='net10.0',[string]$Work=$(Join-Path $env:RUNNER_TEMP 'r12proof'))
$ErrorActionPreference='Stop'; Set-Location (Join-Path $PSScriptRoot '..'); $root=(Get-Location).Path
$vsInst='C:\ProgramData\Microsoft\VisualStudio\Packages\_Instances'
function Restore-VS { if(Test-Path "$vsInst.hidden"){ Rename-Item "$vsInst.hidden" '_Instances' } }
trap { Restore-VS; throw $_ }
$res=Join-Path $root 'results'; if(Test-Path $res){ Remove-Item $res -Recurse -Force }; New-Item -ItemType Directory $res | Out-Null
$L=Join-Path $res "proof-$Tfm.md"; function Log($t){ Write-Host $t; Add-Content $L $t }
if(Test-Path $Work){ Remove-Item $Work -Recurse -Force }; New-Item -ItemType Directory $Work | Out-Null
$sdk=Join-Path $Work 'sdk'; $nuget=Join-Path $Work 'nuget'; $home_=Join-Path $Work 'clihome'; New-Item -ItemType Directory $nuget,$home_ | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile (Join-Path $Work 'di.ps1')
$a=@{Channel=$Channel;InstallDir=$sdk}; if($Quality){ $a.Quality=$Quality }
& (Join-Path $Work 'di.ps1') @a | Out-Null
$nodeDir=Split-Path (Get-Command node).Source; $sys=$env:SystemRoot
$env:PATH="$sdk;$sys\System32;$sys;$sys\System32\WindowsPowerShell\v1.0;$nodeDir"
$env:DOTNET_ROOT=$sdk; $env:DOTNET_ROOT_X64=$sdk; $env:DOTNET_CLI_HOME=$home_; $env:NUGET_PACKAGES=$nuget
$env:DOTNET_MULTILEVEL_LOOKUP='0'; $env:DOTNET_NOLOGO='1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Remove-Item Env:MSBuildSDKsPath,Env:MSBUILD_EXE_PATH,Env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR -ErrorAction SilentlyContinue
$dn=Join-Path $sdk 'dotnet.exe'
# a workload registered machine-wide (Visual Studio on windows-latest) is fine: the build must use OUR package, asserted per publish below
function AssertEmpty($when){ $wl=(& $dn workload list 2>&1 | Out-String); Log "## dotnet workload list ($when; informational, the packs must come from the NuGet cache)"; Log ('~~~'+$wl+'~~~') }
Log "# Ref12.WasmNative proof, $Tfm (Windows)"; Log "SDK: $(& $dn --version)"; AssertEmpty 'before'
$ver='0.0.1-ci'
& $dn pack Ref12.WasmNative -c Release "-p:PackageVersion=$ver" -o artifacts -v:q -nologo | Out-Null; if($LASTEXITCODE){ Restore-VS; exit 1 }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z=[IO.Compression.ZipFile]::OpenRead((Get-ChildItem artifacts/*.nupkg | Select-Object -First 1).FullName); Log ("nupkg entries: " + (($z.Entries | ForEach-Object FullName) -join ', ')); $z.Dispose()
$feed=Join-Path $Work 'feed'; New-Item -ItemType Directory $feed | Out-Null; Copy-Item artifacts/*.nupkg $feed
$w=Join-Path $Work 'proj'; Copy-Item smoke $w -Recurse; Set-Location $w
Set-Content nuget.config "<configuration><packageSources><clear/><add key='local' value='$feed'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>"
Set-Content global.json "{`"msbuild-sdks`":{`"Ref12.WasmNative`":`"$ver`"}}"
Log "NUGET_PACKAGES=$nuget (clean)"
npm install --no-audit --no-fund --no-save --no-package-lock playwright-core@1.50.0 | Out-Null
$code=0
foreach($v in @(@('plain',@()),@('relink',@('-p:WasmBuildNative=true')),@('native',@('-p:WasmBuildNative=true','-p:SmokeNativePkg=true')),@('aot',@('-p:Ref12WasmNativeAot=true')))){
  $n=$v[0]; foreach($d in 'app/obj','app/bin',"out/$n"){ if(Test-Path $d){ Remove-Item $d -Recurse -Force } }
  $t=[Diagnostics.Stopwatch]::StartNew()
  $o=(& $dn publish app -c Release "-p:SmokeTfm=$Tfm" -o "out/$n" @($v[1]) 2>&1 | Out-String)
  if($LASTEXITCODE){ Log "extracted: $((Get-ChildItem $nuget/ref12.wasmnative -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object FullName) -join ', ')"; Log "- ${n}: PUBLISH FAILED"; Log (($o -split "`n" | Where-Object { $_ -match 'error|warning R12' } | Select-Object -Unique -First 15) -join "`n"); $code=1; continue }
  if($n -ne 'plain'){
    # no workload-only warnings, and the toolchain must come from OUR package (the NuGet cache), whatever workload the machine has
    if($o -match "won't be linked in|Publishing without optimizations"){ Log "- ${n}: WORKLOAD WARNING PRESENT"; $code=1 }
    if($o -notmatch 'Ref12\.WasmNative: SDK'){ Log "- ${n}: no 'Ref12.WasmNative:' line in the log"; $code=1 }
    $emcc=($o -split "`n" | Where-Object { $_ -match 'Compiling native assets with' } | Select-Object -First 1)
    if(-not $emcc -or $emcc.ToLower().Replace('/','\') -notlike "*$($nuget.ToLower().Replace('/','\'))*"){ Log "- ${n}: emcc is not from the NuGet cache: $emcc"; $code=1 }
    if($o -notmatch 'Linking with emcc'){ Log "- ${n}: no emcc link step"; $code=1 }
  }
  $f=(Get-ChildItem "out/$n/wwwroot/_framework" -Filter 'dotnet.native.*.wasm' | Select-Object -First 1)
  Log "- ${n}: publish $([int]$t.Elapsed.TotalSeconds) s, dotnet.native.wasm $($f.Length) bytes"
  ($o -split "`n" | Where-Object { $_ -match 'warning R12' } | Select-Object -Unique) | ForEach-Object { Log "  $_" }
  $env:OUT="out/$n"; $env:BROWSER_CHANNEL='msedge'; $s=(node smoke.mjs 2>&1 | Out-String); if($LASTEXITCODE){ $code=1; Log "- smoke ${n}: FAIL"; Log $s } else { Log "- smoke ${n}: OK" }
}
$sz=(Get-ChildItem $nuget -Recurse -File | Measure-Object Length -Sum).Sum
Log "NuGet cache after: $([math]::Round($sz/1MB)) MB; packages: $((Get-ChildItem $nuget -Directory).Name -join ' ')"
AssertEmpty 'after'
Copy-Item $L $root/results/ -ErrorAction SilentlyContinue
Restore-VS; exit $code
