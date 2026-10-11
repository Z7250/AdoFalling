param(
    [string]$JsonDll = "D:\steam\steamapps\common\A Dance of Fire and Ice\umm\Newtonsoft.Json.dll"
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo 'build\tests'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $outDir 'PlannerTests.exe'
& $csc /nologo /target:exe /langversion:5 /optimize+ /nowarn:0649 /out:$exe `
    (Join-Path $repo 'src\AdoFalling\FingeringPlanner.cs') `
    (Join-Path $repo 'src\AdoFalling\NoteInstance.cs') `
    (Join-Path $PSScriptRoot 'PlannerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Planner test compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Planner checks failed' }

if (-not (Test-Path -LiteralPath $JsonDll))
{
    throw 'Pass -JsonDll with a .NET Framework compatible Newtonsoft.Json.dll to run reader integration checks'
}
$chartExe = Join-Path $outDir 'ChartTests.exe'
Copy-Item -LiteralPath $JsonDll -Destination (Join-Path $outDir 'Newtonsoft.Json.dll') -Force
& $csc /nologo /target:exe /langversion:5 /optimize+ /nowarn:0649 /out:$chartExe /reference:$JsonDll `
    (Join-Path $repo 'src\AdoFalling\FingeringPlanner.cs') `
    (Join-Path $repo 'src\AdoFalling\NoteInstance.cs') `
    (Join-Path $repo 'src\AdoFalling\MacroPlan.cs') `
    (Join-Path $repo 'src\AdoFalling\ChartReader.cs') `
    (Join-Path $repo 'src\AdoFalling\FallingView.cs') `
    (Join-Path $PSScriptRoot 'ChartTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Chart test compilation failed' }
& $chartExe (Join-Path $outDir 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Chart integration checks failed' }
