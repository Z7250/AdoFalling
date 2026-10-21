param(
    [string]$JsonDll = "D:\steam\steamapps\common\A Dance of Fire and Ice\umm\Newtonsoft.Json.dll",
    [string]$RegressionChart = ""
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo 'build\tests'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $outDir 'PlannerTests.exe'
& $csc /nologo /target:exe /langversion:5 /optimize+ /nowarn:0649 /out:$exe `
    (Join-Path $repo 'src\AdoFalling\FingeringPlanner.cs') `
    (Join-Path $repo 'src\AdoFalling\ChordVisuals.cs') `
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
    (Join-Path $repo 'src\AdoFalling\ChordVisuals.cs') `
    (Join-Path $repo 'src\AdoFalling\NoteInstance.cs') `
    (Join-Path $repo 'src\AdoFalling\MacroPlan.cs') `
    (Join-Path $repo 'src\AdoFalling\ChartReader.cs') `
    (Join-Path $repo 'src\AdoFalling\GameReflection.cs') `
    (Join-Path $repo 'src\AdoFalling\FallingView.cs') `
    (Join-Path $repo 'src\AdoFalling\FallingEngine.cs') `
    (Join-Path $PSScriptRoot 'ChartTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Chart test compilation failed' }
$chartArguments = @((Join-Path $outDir 'fixtures'))
if ($RegressionChart) { $chartArguments += (Resolve-Path -LiteralPath $RegressionChart).Path }
& $chartExe @chartArguments
if ($LASTEXITCODE -ne 0) { throw 'Chart integration checks failed' }

$fakeGame = Join-Path $outDir 'Assembly-CSharp.dll'
& $csc /nologo /target:library /langversion:5 /out:$fakeGame (Join-Path $PSScriptRoot 'FakeGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'Game reflection fixture compilation failed' }
$runtimeExe = Join-Path $outDir 'RuntimeTests.exe'
& $csc /nologo /target:exe /langversion:5 /optimize+ /nowarn:0649 /out:$runtimeExe /reference:$JsonDll /reference:$fakeGame `
    (Join-Path $repo 'src\AdoFalling\Main.cs') `
    (Join-Path $repo 'src\AdoFalling\FallingPlugin.cs') `
    (Join-Path $repo 'src\AdoFalling\FallingConfig.cs') `
    (Join-Path $repo 'src\AdoFalling\FingeringPlanner.cs') `
    (Join-Path $repo 'src\AdoFalling\ChordVisuals.cs') `
    (Join-Path $repo 'src\AdoFalling\NoteColorEditor.cs') `
    (Join-Path $repo 'src\AdoFalling\SettingsFile.cs') `
    (Join-Path $repo 'src\AdoFalling\GameClock.cs') `
    (Join-Path $repo 'src\AdoFalling\GameState.cs') `
    (Join-Path $repo 'src\AdoFalling\GameReflection.cs') `
    (Join-Path $repo 'src\AdoFalling\ControlPanel.cs') `
    (Join-Path $repo 'src\AdoFalling\PanelControls.cs') `
    (Join-Path $repo 'src\AdoFalling\AutoPlayDriver.cs') `
    (Join-Path $repo 'src\AdoFalling\NoteInstance.cs') `
    (Join-Path $PSScriptRoot 'RuntimeHost.cs') `
    (Join-Path $PSScriptRoot 'RuntimeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UMM runtime test compilation failed' }
& $runtimeExe (Join-Path $outDir 'runtime-fixtures')
if ($LASTEXITCODE -ne 0) { throw 'UMM runtime checks failed' }
