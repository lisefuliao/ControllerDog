# 抖抖的抖

抖抖的抖是一个 Windows 本地 WPF 手柄按键映射工具，作者署名：马德锦大神。

它只做一件事：把手柄输入一对一映射到键盘按键或鼠标按键。项目不包含宏、连发、自动压枪、自动操作、游戏内存读写、注入、驱动级伪装、虚拟 HID 输出或任何复杂外挂功能。

## 当前重构重点

- XInput 0~3 并行扫描，优先使用 Vortice.XInput。
- HID 检测优先使用 Usage Page / Usage，再结合 VID/PID 和名称辅助识别。
- 支持 DualShock 4、DualSense / DSE、Xbox / XInput、常见 generic HID controller 的展示与基础读取。
- 输入线程、输出线程、UI 刷新线程分离。
- ButtonDown / ButtonHeld / ButtonUp 状态机。
- SendInput 只在状态变化时发送。
- 设备断开、异常、停止映射、退出程序时强制释放全部输出。
- Watchdog 每 50ms 检查防粘键状态。
- 映射编辑使用弹窗，支持图形点击、直接捕获输入、快速推荐。
- PerMonitorV2 DPI Awareness，适配 2K / 4K 高分屏。
- 提供 `win-x64` 自包含单文件发布配置。

## 技术栈

- .NET 8
- WPF
- Vortice.XInput
- HidSharp
- Windows SendInput

## 编译

安装 Visual Studio 2022 Community 时勾选“.NET 桌面开发”，然后打开：

```text
抖抖的抖.sln
```

在 Visual Studio 中：

1. 等待 NuGet 自动还原。
2. 配置选择 `Release`。
3. 按 `Ctrl+Shift+B` 编译。

命令行：

```powershell
dotnet build .\抖抖的抖.sln -c Release
```

## 发布单文件 exe

Visual Studio：

1. 右键项目“抖抖的抖”。
2. 选择“发布”。
3. 选择发布配置 `win-x64-single-file`。
4. 点击“发布”。

命令行：

```powershell
dotnet publish .\抖抖的抖\抖抖的抖.csproj -p:PublishProfile=win-x64-single-file
```

发布输出路径：

```text
抖抖的抖\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\抖抖的抖.exe
```

## 配置

首次启动会生成用户配置：

```text
%APPDATA%\抖抖的抖\config.json
```

默认映射：

- `LT / L2` -> 鼠标右键
- `RT / R2` -> 鼠标左键
- `Back / Share / Create` -> `M` 键

软件会自动保存上次配置，也可以手动点击“保存配置”和“加载配置”。

## 设备识别建议

- Xbox / XInput 手柄优先走 XInput。
- DS4 / DualSense 如果被其它工具模拟成 Xbox 手柄，会显示为 XInput，这是正常现象。
- 原生 HID 模式下会优先使用 HID Usage Page / Usage 判断是否是控制器。
- 蓝牙 HID 报告格式差异较大，排查时建议先用 USB。
- 如果 generic HID 手柄按键不准，需要补充该设备的报告解析表。

## 高轮询率说明

轮询率选项：

- 250 Hz = 4 ms
- 500 Hz = 2 ms
- 1000 Hz = 1 ms
- 2000 Hz = 0.5 ms
- 4000 Hz = 0.25 ms
- 8000 Hz = 0.125 ms

Windows 用户态程序无法保证绝对稳定 8000Hz，`8000 Hz（实验）`只是目标循环频率。高频模式会使用高精度计时 + 少量自旋 + cancellation token 控制，仍建议日常使用 `1000 Hz` 或 `500 Hz`。

## 卡键 / 粘键处理

如果感觉卡键：

1. 点击“停止映射”。
2. 重新插拔手柄。
3. 降低轮询率。
4. 关闭其它键鼠映射软件。

软件内部会在停止、退出、设备断开、输入异常、Watchdog 检测异常时释放所有按下的键盘和鼠标输出。
