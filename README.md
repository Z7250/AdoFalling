# AdoFalling

《A Dance of Fire and Ice》的下落式读谱叠加层，使用 **Unity Mod Manager（UMM）** 加载。当前版本为 **0.7.0**。

叠加层显示八条轨道、下落音符、长按尾迹和命中特效，原版星球与路径仍然可见。音符轨道表示建议指法；实际输入和判定仍由游戏处理，模组不会强制检查是否按了对应的轨道键。

## 安装

1. 为游戏安装 Unity Mod Manager。此版本要求 UMM **0.32.5 或更新版本**；编译使用本机游戏 3.3.1 的 Unity 6000 程序集与 UMM 0.32.5。
2. 将 `build/AdoFalling-0.7.0-UMM.zip` 拖入 UMM 的 Mods 页安装，或者把 `build/AdoFalling` 文件夹复制到游戏的 `Mods` 目录。
3. 启动游戏，在 UMM 中启用 AdoFalling。首次加载会在模组目录生成 `settings.json`。

手动安装后的结构：

```text
A Dance of Fire and Ice/
  Mods/
    AdoFalling/
      Info.json
      AdoFalling.dll
      settings.json       # 首次加载生成，不在安装包中
```

从旧版本迁移时，移走旧的 `BepInEx/plugins/AdoFalling.dll`，避免两份叠加层同时运行。其他模组的加载器和文件无需处理。旧的 BepInEx `.cfg` 不会自动导入，可在 F5 面板重新设置或将所需数值转写到 JSON。

在 UMM 中禁用会销毁叠加层，解除本模组的命中回调，恢复启用自动播放之前的状态，并保存配置。重新启用会建立新的运行实例。

## 八键指法

屏幕默认从左到右是 `A S D F J K L ;`。左右食指分别对应 **F / J**。分配器按谱面的局部 BPM、速度变化和音符时间工作，默认以八分音符为换手周期，将细分音符分成同手组，组内由外向内轮指。

| 每个换手周期内的音符数 | 左手 | 右手 | 常见节奏 |
| --- | --- | --- | --- |
| 1 | F | J | 八分音符 |
| 2 | DF | KJ | 十六分音符 |
| 3 | SDF | LKJ | 二十四分音符 |
| 4 | ASDF | ;LKJ | 三十二分音符 |

- 八分连打保持 `F J F J`；每只手在自己的下一次八分轮次继续用食指。
- 八分接十六分可形成 `F J DF KJ`，保持左右手的组节奏。
- 反拍允许调整分组边界与相位，减少边界附近过短的换手间隔。
- 更密的段落会缩短换手周期，每组最多四个音符。
- 长按占用的键在松手前会被避开，必要时使用其他手指。
- 只改变轨道分配，保留音符时间、顺序和长按时长。原谱的同时音符不会被人为错开；同时/近同时的左右手按下是优化目标，无法在所有谱面中消除。

默认映射为 `Fingering`。F5 面板可切换到循环分配或按方块角度分配。旧的 `Macro` 映射值仍按新指法处理。

## 控制

| 默认按键 | 功能 |
| --- | --- |
| F5 | 打开/关闭中文控制面板；关闭时保存 |
| F8 | 显示/隐藏叠加层 |
| F4 | 加载 `Chart/FilePath` 指定的 `.adofai`，开始/重启文件模式 |
| F6 | 将当前时间对齐信息写入 UMM 日志 |
| F7 / F9 | 调整音符时间偏移 |
| End | 音符时间偏移归零 |
| PageUp / PageDown | 增减开场提前量系数 |
| - / = | 长按显示延后/提前 |
| Backspace | 长按偏移归零 |
| [ / ] | 缩短/加长长按长度系数 |

Shift 将时间微调步长从 0.01 秒增至 0.05 秒。面板内可用上下箭头选择、左右箭头调整、Enter 触发开关或按钮，也支持鼠标操作。UMM 的模组选项页同样提供控制面板入口。

面板可调节轨道布局、下落速度、显隐、透明度、时间偏移、长按显示、换手保护窗口、自动播放及诊断信息。开关叠加层不会关闭控制面板入口。

## 配置与读谱

配置位于 `Mods/AdoFalling/settings.json`，按 `General`、`Layout`、`Chart`、`Tuning`、`View`、`Style`、`Feedback` 分组。枚举使用名称，颜色使用 `r/g/b/a` 对象，通常取值为 0～1。单个无效配置项使用默认值并写日志；JSON 损坏时先保留 `settings.json.invalid.bak`，再生成默认配置。

示例（首次生成的文件包含全部选项）：

```json
{
  "General": {
    "ToggleKey": "F8",
    "LoadKey": "F4",
    "AutoPlay": false
  },
  "Layout": {
    "LaneMapping": "Fingering",
    "NoteSpeed": 620.0,
    "LaneWidth": 70.0
  },
  "Chart": {
    "FingeringWindowMs": 180.0,
    "FilePath": "D:\\charts\\example.adofai"
  },
  "Style": {
    "NoteColor": "255,0,0",
    "LaneColor": { "r": 0.05, "g": 0.06, "b": 0.09, "a": 1.0 }
  }
}
```

`FingeringWindowMs` 是避免过短换手的保护参数，并受局部换手周期限制；音符细分由局部节拍推导。普通八分不会因 BPM 升高而自动变成轮指。

默认直接读取游戏已建立的谱面时间轴，显示时钟每帧读取游戏歌曲时间，命中特效使用游戏的 `scrPlayer.onHit`。文件模式复用相同的轨道分配器，但文件解析仅覆盖角度、速度、转向、暂停等已有支持，不完整覆盖长按和所有特殊事件；有复杂事件的谱面优先使用默认读取方式。F4 把文件首音锚定到按键时刻，应在首音时启动；再次按下会重启时间轴。

在游戏关闭时编辑 JSON，下一次启动加载修改。面板关闭、UMM 保存配置、禁用与卸载都会保存当前设置。更新安装包只替换清单与 DLL，保留已有 `settings.json`。

## 构建与验证

Windows 上使用系统 .NET Framework C# 编译器，不需要安装 .NET SDK：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1

# 指定游戏位置；UMM DLL 默认从游戏 Managed/UnityModManager 或 umm 目录寻找
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -GameDir "D:\games\A Dance of Fire and Ice"

# 可显式传入 UMM DLL 所在目录
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -UmmDir "D:\tools\UnityModManager"

# 编译后复制到游戏 Mods/AdoFalling
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install

# 运行算法、读谱和 UMM 生命周期检查
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run.ps1
```

产物为 `build/AdoFalling.dll`、`build/AdoFalling/` 和 `build/AdoFalling-0.7.0-UMM.zip`。插件引用游戏的 Unity / Newtonsoft.Json 和 UMM 程序集；无需额外补丁库。

`tests/run.ps1` 支持 `-JsonDll` 指定兼容 .NET Framework 的 Newtonsoft.Json DLL，默认使用游戏 `umm` 目录中的版本。当前 39 项检查覆盖指法、反拍、局部变速、长按避让、八轨布局、JSON 往返与异常恢复、UMM 加载/启停/卸载、回调清理和自动播放状态恢复。生命周期检查使用托管宿主与反射夹具，不启动 Unity；本次 UMM 改造尚需实机确认渲染、输入及与其他模组共存。
