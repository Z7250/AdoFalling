# Build and package the Unity Mod Manager mod.
# powershell -ExecutionPolicy Bypass -File build.ps1
# powershell -ExecutionPolicy Bypass -File build.ps1 -Install
param(
    [switch]$Install,
    [string]$GameDir = "D:\steam\steamapps\common\A Dance of Fire and Ice",
    [string]$UmmDir = ""
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$srcDir = Join-Path $repo "src\AdoFalling"
$outDir = Join-Path $repo "build"
$managed = Join-Path $GameDir "A Dance of Fire and Ice_Data\Managed"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not $UmmDir) {
    $UmmDir = Join-Path $managed "UnityModManager"
    if (-not (Test-Path -LiteralPath (Join-Path $UmmDir "UnityModManager.dll"))) {
        $UmmDir = Join-Path $GameDir "umm"
    }
}
foreach ($path in @($csc, $managed, (Join-Path $UmmDir "UnityModManager.dll"))) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing compilation dependency: $path" }
}

$manifestPath = Join-Path $repo "Info.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Id -ne "AdoFalling8K" -or $manifest.AssemblyName -ne "AdoFalling8K.dll" -or $manifest.EntryMethod -ne "AdoFalling8K.Main.Load") {
    throw "Info.json does not match the UMM entry point"
}
$pluginSource = Get-Content -LiteralPath (Join-Path $srcDir "FallingPlugin.cs") -Raw
if ($pluginSource -notmatch ('Version = "' + [regex]::Escape($manifest.Version) + '"')) {
    throw "Info.json and PluginInfo versions differ"
}

$refs = @(
    (Join-Path $managed "netstandard.dll"),
    (Join-Path $managed "System.Runtime.dll"),
    (Join-Path $managed "Newtonsoft.Json.dll"),
    (Join-Path $UmmDir "UnityModManager.dll")
)
foreach ($module in @("UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.IMGUIModule.dll",
                     "UnityEngine.InputLegacyModule.dll", "UnityEngine.TextRenderingModule.dll",
                     "UnityEngine.AudioModule.dll")) {
    $refs += Join-Path $managed $module
}
foreach ($path in $refs) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing compilation dependency: $path" }
}
$sources = Get-ChildItem -LiteralPath $srcDir -Filter *.cs | Select-Object -ExpandProperty FullName
$refArg = ($refs | ForEach-Object { '"' + $_ + '"' }) -join ","
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dll = Join-Path $outDir $manifest.AssemblyName
$log = Join-Path $outDir "csc.log"
Write-Host "Compiling UMM mod -> $dll"
& $csc /nologo /target:library /langversion:5 /optimize+ /out:$dll /reference:$refArg $sources 2>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) { throw "Compilation failed; see $log" }

$packageDir = Join-Path $outDir $manifest.Id
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
$packageDll = Join-Path $packageDir $manifest.AssemblyName
$packageInfo = Join-Path $packageDir "Info.json"
Copy-Item -LiteralPath $dll -Destination $packageDll -Force
Copy-Item -LiteralPath $manifestPath -Destination $packageInfo -Force
$zip = Join-Path $outDir ($manifest.Id + "-" + $manifest.Version + "-UMM.zip")
Compress-Archive -LiteralPath $packageInfo,$packageDll -DestinationPath $zip -Force
Write-Host "Packaged -> $zip"

if ($Install) {
    $modDir = Join-Path $GameDir ("Mods\" + $manifest.Id)
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    Copy-Item -LiteralPath $packageDll,$packageInfo -Destination $modDir -Force
    Write-Host "Installed -> $modDir (existing settings.json preserved)"
}
