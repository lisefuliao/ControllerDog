# 抖抖的抖

抖抖的抖是一个本地手柄按键到键盘/鼠标的一对一映射工具。它只做普通按键映射：手柄按下时输出目标按下，手柄释放时输出目标释放，保持按住时不重复发送。

本项目不包含宏、连点、压枪、自动化脚本、游戏内存读写、进程注入、DLL 注入、隐藏进程、隐藏窗口、驱动、虚拟 HID、内核驱动、设备栈修改、全局低层键鼠 Hook、SetWindowsHookEx、固定时间输入序列、画面识别、游戏窗口扫描。

## 功能状态

- XInput / 类 Xbox 手柄：通过 Vortice.XInput 读取。
- DS4 / DualSense / DSE：通过 HidSharp 做基础 VID/PID 识别和常见 USB 报告解析。
- 未知 HID 手柄：可识别展示，第一版不保证能解析所有按键。
- 输出方式：Windows SendInput，且只在状态变化时发送。
- 防粘键：停止映射、程序退出、设备断开、输入线程异常、Watchdog 检测异常时都会释放已按下输出。
- UI 刷新限制：界面约 60 FPS 刷新，不跟随高轮询率刷新。

## NuGet 依赖

- Vortice.XInput 3.6.2
- HidSharp 2.6.4

Visual Studio 打开解决方案后会自动还原 NuGet 包。如果没有自动还原，请右键解决方案，选择“还原 NuGet 包”。

## 文件结构

```text
抖抖的抖.sln
抖抖的抖/
  抖抖的抖.csproj
  app.manifest
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  README.md
  config.default.json
  Models/
    MappingConfig.cs
    MappingEntry.cs
    ControllerState.cs
    PollingRateOption.cs
    InputTarget.cs
    ControllerType.cs
  Services/
    ControllerInputService.cs
    XInputControllerService.cs
    HidControllerService.cs
    InputOutputService.cs
    MappingEngine.cs
    ConfigService.cs
    WatchdogService.cs
    ControllerDetectionService.cs
  ViewModels/
    MainViewModel.cs
  Controls/
    ControllerPreviewControl.xaml
    ControllerPreviewControl.xaml.cs
    StatusBadge.xaml
    StatusBadge.xaml.cs
  Themes/
    Colors.xaml
    ButtonStyles.xaml
    ComboBoxStyles.xaml
    CardStyles.xaml
    DataGridStyles.xaml
  Utils/
    HighPrecisionTimer.cs
    KeyCodeHelper.cs
    SafeReleaseManager.cs
```

## 小白编译教程

1. 打开浏览器，搜索并下载 Visual Studio 2022 Community。
2. 安装器里勾选“.NET 桌面开发”工作负载。
3. 安装完成后双击 `抖抖的抖.sln`。
4. 第一次打开时等待右下角 NuGet 自动还原结束。
5. 如果没有自动还原，右键解决方案“抖抖的抖”，点击“还原 NuGet 包”。
6. 顶部配置选择 `Debug` 或 `Release`，平台保持 `Any CPU`。
7. 按 `Ctrl+Shift+B` 编译。
8. 按 `F5` 调试运行，或按 `Ctrl+F5` 直接运行。

## 发布单文件 exe

Visual Studio 方式：

1. 右键项目“抖抖的抖”，选择“发布”。
2. 目标选择“文件夹”。
3. 部署模式选择“自包含”。
4. 目标运行时选择 `win-x64`。
5. 文件发布选项勾选“生成单个文件”。
6. 点击“发布”。

命令行方式：

```powershell
dotnet publish .\抖抖的抖\抖抖的抖.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

发布后的 exe 通常在：

```text
抖抖的抖\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

## 默认映射

首次启动会自动生成配置文件：

```text
%APPDATA%\抖抖的抖\config.json
```

默认映射：

- `LT / L2` -> 鼠标右键
- `RT / R2` -> 鼠标左键
- `Back / Share / Create` -> `M` 键

修改默认映射有两种方式：

1. 在软件映射表里直接修改，然后点击“保存配置”。
2. 修改项目里的 `config.default.json`，重新编译或复制到输出目录后首次启动使用。

键盘目标可以写 `A`、`M`、`Space`、`Enter`、`Escape`、`LeftShift`、`F1` 等。鼠标目标使用：

- `LeftButton`
- `RightButton`
- `MiddleButton`
- `XButton1`
- `XButton2`

## 设备识别不到怎么办

1. 先确认 Windows “设置 -> 蓝牙和设备”里能看到手柄。
2. 有线连接比蓝牙更稳定，排查时建议先用 USB。
3. Xbox / XInput 手柄会优先走 XInput。
4. DS4 / DualSense 如果被第三方工具模拟成 Xbox 手柄，软件会显示为 XInput，这是正常的。
5. 关闭占用 HID 设备的其它映射软件后重启本软件。
6. 如果仍然识别不到，第一版可能没有覆盖该设备的 HID 报告格式，可在后续增强 `HidControllerService.cs`。

## 卡键 / 粘键处理

软件已经加入多层释放保护：

- 停止映射时释放全部按下输出。
- 程序退出时释放全部按下输出。
- 设备断开时释放全部按下输出。
- 输入线程异常时释放全部按下输出。
- Watchdog 每 50ms 检查一次输出状态。

如果你感觉卡键：

1. 点击“停止映射”。
2. 重新插拔手柄。
3. 降低轮询率到 `1000 Hz` 或 `500 Hz`。
4. 关闭其它键鼠映射软件，避免多个工具同时发送输入。

## 高轮询率和 CPU 占用

轮询率含义：

- 250 Hz = 4 ms
- 500 Hz = 2 ms
- 1000 Hz = 1 ms
- 2000 Hz = 0.5 ms
- 4000 Hz = 0.25 ms
- 8000 Hz = 0.125 ms

本项目使用 `Stopwatch` + `Thread.Yield` + `SpinWait` 的混合循环，不只依赖 `Thread.Sleep(1)`。但是 Windows 用户态程序无法保证绝对稳定 8000Hz，8000Hz 只是目标循环频率，不保证所有电脑、USB 控制器、蓝牙链路都能稳定达到。

如果 CPU 占用高：

1. 优先使用 `1000 Hz`。
2. 如果只玩普通游戏，`500 Hz` 通常已经足够。
3. 蓝牙手柄不建议开很高轮询率。
4. 关闭不必要的后台程序。

## 切换 XInput / DS4 / DualSense 模式

第一版采用自动模式：

- 检测到 XInput 时优先使用 XInput。
- 没有 XInput 时尝试 HID。
- Sony VID `054C` 的 DS4、DualSense、DSE 会尽量按型号展示。

如果想让 DS4 / DualSense 以原生 HID 显示，请关闭会把手柄模拟成 Xbox 手柄的第三方工具。如果想走 XInput，请使用系统或第三方工具把手柄暴露为 XInput 设备。

## 后续增强点

- 增加更多 HID 手柄的报告解析表。
- 增加手动选择设备。
- 增加更友好的目标按键选择窗口。
- 增加更多设备名称和 VID/PID 识别规则。
