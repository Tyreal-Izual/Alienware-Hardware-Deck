# 开发指南

[English](DEVELOPMENT.md) | **简体中文**

HardwareDeck 是一个 Windows .NET Framework 4.8 可执行程序，运行时加载 WPF 视图。
构建使用 Windows Framework C# 编译器，无需 NuGet 还原或 .NET SDK。
项目作为独立仓库开发和发布。

## 构建与验证

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -OutputPath .local/build/HardwareDeck.exe
powershell -NoProfile -ExecutionPolicy Bypass -File Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Packaging.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Display.Tests.ps1
```

关闭正在运行的监控窗口后，不带 `-OutputPath` 执行 `Build.ps1` 可更新根目录的可执行文件。
构建会复制视图和运行配置，仅在传感器标签文件缺失时创建它，并嵌入应用图标。
本地传感器偏好会保留。启动脚本仅在需要时构建；修改源码后需要重新构建。

[scripts/Compiler.ps1](scripts/Compiler.ps1) 集中管理应用和测试脚本使用的编译器路径、
Framework 引用和生产源码发现逻辑。直接位于 `src` 下的所有生产 C# 文件会一同编译。
核心测试程序选择自己的入口点，因此可以访问内部类，而不必维护第二份生产文件清单。
界面测试使用实际构建的应用程序集。

| 检查 | 范围 |
| --- | --- |
| `Test.ps1` | 传感器契约、窗口状态、网络速率、预设策略、读回、worker 生命周期 |
| `tests/Packaging.Tests.ps1` | 首次构建、资源复制、启动器行为、无效 worker 请求 |
| `tests/Display.Tests.ps1` | 实际 WPF 布局、功耗／OC 状态、过期数据与 1080p 缩放 |

自动化测试使用样例数据或模拟硬件后端，不会改变硬件模式。
界面预览写入 `.local/display-tests/renders`。
CI 执行相同命令，但没有 Alienware 硬件。实机验收见 [VERIFICATION.md](VERIFICATION.zh-CN.md)。

只读诊断和预览导出：

```powershell
.\HardwareDeck.exe --probe C:\path\snapshot.json
.\HardwareDeck.exe --preview C:\path\preview.png
```

诊断会采集快照后退出。预览捕获的是实时面板，而非测试样例数据。
这两种操作都不会修改保存的窗口位置。完整传感器访问仍需管理员权限。
诊断内容包含机器信息，默认保留在本地。

## 源码结构

| 区域 | 文件与职责 |
| --- | --- |
| 入口 | [Program.cs](src/Program.cs)：面板、诊断／预览及内部 worker 命令路由 |
| 面板 | [DeckWindow.cs](src/DeckWindow.cs)：窗口生命周期、采样、布局和读数更新 |
| 界面分区 | [DeckWindow.Presets.cs](src/DeckWindow.Presets.cs)：控制状态与请求；[DeckWindow.History.cs](src/DeckWindow.History.cs)：曲线；[DeckWindow.Tiles.cs](src/DeckWindow.Tiles.cs)：可复用传感器卡片 |
| 视图 | [View.xaml](View.xaml)：英文标签、布局和样式 |
| 采样 | [Collector.cs](src/Collector.cs)：协调各读取器；[Snapshot.cs](src/Snapshot.cs)：共享数据和显式快照序列化 |
| 传感器 | [SystemReader.cs](src/SystemReader.cs)、[CpuTelemetry.cs](src/CpuTelemetry.cs)、[XtuVoltageReader.cs](src/XtuVoltageReader.cs)、[NvidiaReader.cs](src/NvidiaReader.cs)、[NetworkReader.cs](src/NetworkReader.cs) |
| 固件 | [AwccReader.cs](src/AwccReader.cs)：WMI 温度、风扇与受保护的散热写入；[ThermalModes.cs](src/ThermalModes.cs)：发现的预设元数据 |
| 配置 | [SensorConfiguration.cs](src/SensorConfiguration.cs)：传感器标签和 CPU 温度源选择 |
| 预设规则 | [PresetControl.cs](src/PresetControl.cs)：标准模式常量／顺序／名称、目标解析和校验事务 |
| 预设执行 | [PresetBackend.cs](src/PresetBackend.cs)：真实读取器与操作；[AwccPresetBridge.cs](src/AwccPresetBridge.cs)：AWCC 原生控件和私有桌面所有权 |
| Worker 协议 | [PresetWorker.cs](src/PresetWorker.cs)：有界 JSON 输入、进程隔离、会话互斥锁、结果处理和超时 |
| CPU 读回 | [CpuPresetReader.cs](src/CpuPresetReader.cs)：只读 XTU 契约与 OEM 配置匹配 |
| 窗口状态 | [WindowPreferences.cs](src/WindowPreferences.cs)、[WindowPlacementSession.cs](src/WindowPlacementSession.cs)：持久化、屏幕身份和 DPI 恢复 |
| 外观 | [WindowAppearance.cs](src/WindowAppearance.cs)：原生边框与内嵌图标 |

面板分区是同一个 `DeckWindow` 类的 partial 声明。
它们共享一套采样生命周期和请求状态；拆分文件不会创建额外窗口、采集器或控制路径。

## 执行路径

**监控：**`Collector.Sample` 将系统、CPU、GPU、AWCC 和网络读数汇总为快照。
散热模式变化时，会强制刷新 CPU 功耗上限读回。
窗口在自身 dispatcher 上更新，曲线历史在内存中保留 120 秒。
较新的 worker 结果优先于较旧的采样快照。

**预设切换：**界面通过 `PresetPolicy` 解析请求，锁定控件并启动 `PresetWorker`。
Worker 获取最新状态，使用 `LocalPresetBackend` 调用 AWCC 或允许列表内的散热写入接口。
连续四次匹配读回后才确认完成。随后界面使读回缓存失效，并显示实际状态。
后台桥接仅拥有和清理自己启动的前端。

硬件相关规则应保留在预设／固件层。
界面代码应使用读回结果和共享模式目录，不应根据被点击的按钮推断 OC 状态，也不应重复定义固件 ID。
修改 worker 时，应保留请求边界、失败状态和所有权检查。
详细契约见 [PRESET-CONTROLS.md](PRESET-CONTROLS.zh-CN.md)。

## 约定与本地文件

C# 使用四空格缩进、展开的控制流，以及单独占行的大括号，
具体见 [.editorconfig](.editorconfig)。每个读取器或传输层应专注于单一来源。
新增生产文件应直接放在 `src` 中，应用与核心测试构建会自动发现。
界面文字保持英文，对外可见的变更记录在 [CHANGELOG.md](CHANGELOG.zh-CN.md)。

图标源文件为 [assets/HardwareDeck.svg](assets/HardwareDeck.svg)，使用
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Icon.ps1` 重新生成。

可执行文件、本地传感器标签、检查输出和测试产物均被 Git 忽略。
开发输出放在 `.local`。
用户窗口偏好与启动错误位于 `%LOCALAPPDATA%\HardwareDeck`；
正常监控不会定期导出数据。

## 文档语言

英文文件（`*.md`）是默认入口。每份文档都有对应的简体中文文件（`*.zh-CN.md`），
标题下方提供语言切换。
修改文档时，应同步更新两个版本，保持章节覆盖、命令、标识符、配置示例、日期和验证边界一致。
中文文档链接应指向对应中文版；源码和外部链接保持原目标。
上游许可证声明保留原文。
