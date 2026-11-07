# AWCC 集成架构

[English](AWCC-INTEGRATION.md) | **简体中文**

已在 Aurora R16／i9-14900KF 上，针对本地安装的 AWCC 6.14.105.0 验证。
当前控制行为和验收结果见 [PRESET-CONTROLS.md](PRESET-CONTROLS.zh-CN.md)；
OEM 配置对比见 [AWCC-LOCAL-PROFILES.md](AWCC-LOCAL-PROFILES.zh-CN.md)。

## AWCC 原生流程

前端读取应用模式和活动游戏 GUID，然后调用
`IPerformancePresets.SetPerformancePreset(gameId, preset)`。
退出 Custom 时还会调用 `IAppState.SetCustomMode(false)`。
系统 GUID 为 `00000000-0000-0000-0000-000000000001`；
将其写死会遗漏游戏上下文。

| 界面标签 | PerformancePreset | 固件散热模式 |
| --- | --- | --- |
| Quiet | Quiet = 1 | 163 |
| Balanced | Normal = 2 | 160 |
| Performance | Fast = 3 | 161 |

插件保存选择，并向 `PerformanceControl` 发出通知。
这一独立事件循环解析 CPU、GPU、内存和散热配置，对比当前与目标 ID，
并按该顺序应用发生变化的组件。
Setter 返回不代表硬件已经完成应用；失败和重启原因另行处理。

## 桥接为何调用原生控件

前端通过 Dell 插件管理器获取服务代理。
本地检查发现，该传输层和更底层的 OCControl 本地 RPC 路径都校验调用方签名。
OCControl 使用 `WinTrust.VerifyProcess(pid, allowedPublicKeyPins)`。
通过已安装 RPC 客户端进行的只读 getter 探针未能建立可用的直接访问。
没有修改信任存储、签名检查、厂商二进制、服务权限或进程身份。

WinUI 预设列表提供 Invoke。
项目点击处理器设置 `_userClick`，使随后发生的选择变化能够应用预设。
仅改变选择可能只更新界面，而不进入真正的应用流程。
因此桥接要求 `InvokePattern`，预设操作不会回退为单纯选择。

应用／游戏上下文和完整应用顺序由 AWCC 自己处理，
不由 HardwareDeck 重建，也不独立写入 OC 数值。

## 后台执行

每次变更使用有时间边界的 worker 进程和会话内互斥锁。
普通监控不会持续保留私有 AWCC 前端。

- 已验证属于用户的前端会被复用并最小化，前提是它提供 Dashboard／Performance 控件。
  不跳转其他页面，也不关闭对话框，以免丢弃编辑内容或接受提示。
- 如果没有现有前端，worker 会创建私有 Windows 桌面并在其中启动 AWCC，
  不切换输入桌面。新进程先以挂起状态创建，分配到 Windows job 对象后再恢复执行。
- 只终止 worker 拥有的前端。Job 关闭会清理其所属进程；
  启动失败时也会使用创建时保留的确切挂起进程句柄进行终止。
  用户前端和 Dell 服务会保留。
- 结果管道关闭会被安静处理；窗口关闭后拒绝排队的界面更新；
  晚到的成功结果不能覆盖超时。Worker 观察到取消后，协作式停止检查会阻止新的桥接写入。

不能假定关闭前端就会取消 AWCC 服务已经接受的操作。
硬件变更不是原子操作。失败状态保持未验证，后续请求必须重新获取最新状态。

冷启动检查显示，私有桌面上的控件约四秒后可用，输入桌面上没有 AWCC 窗口。
随后通过 worker 验证了完整切换；确切范围记录在验收报告中。

## 读回与独立散热

校验器先刷新硬件读取缓存，再读取 53 个关键 XTU 当前值以及 WMI 散热模式。
OC 身份由倍率、IccMax、电压模式与偏移判断。
BIOS 散热功耗单独校验：在已审查机器上，Quiet／Balanced／Performance
分别使用 170／180／253 W PL1，PL2 均为 253 W。

Performance 散热配合 OC Off 时，AWCC Balanced 先建立默认 OC 设置；
协调器确认后，再通过允许列表内的散热写入接口应用 Performance。
最终 PL1 为 253 W，而不是 Off 配置文件中的 180 W。
主预设仍使用 AWCC 原生完整流程。

请求必须连续四次读回匹配才确认成功。
散热模式变化会强制刷新 CPU 数据，避免新散热状态与旧 CPU 缓存组合。
AWCC 自动化可以覆盖独立组合；面板会跟随实际读回更新。

当 GPU 能力标志为 false 时，调参器会直接返回成功而不执行调参；
本机 RTX 4090 即为这种情况，因此成功结果不能证明 GPU 频率提高。
检查到的运行时也不支持内存 OC。
OC02 通过 AWCC 原生 Custom CPU 编辑器作为手动 CPU 选项提供，主预设不会选择 OC02。

执行 OC02 时，worker 要求进入 Custom 前已处于 Performance 散热，
随后打开 CPU Advanced View，选择 OC2 并执行 SAVE。
Custom 应用其保存的散热策略后，worker 会重新建立并验证 Performance 散热，
并在保存前再次检查 P。
这一流程要求使用私有前端：用户已打开的 AWCC 窗口保持原样，请求会提示先将其关闭。
仅用于显示的 Performance／Thermal 视图偏好会在操作结束后恢复。
AWCC 会保留所选 Custom CPU 预设；
切换主预设时退出 Custom，应用对应主预设原本的 Off/OC01 映射。
XTU 读取器同时检查 16 个独立 E 核倍率和 16 个活动核数限制，
以区分 OC01（44）和 OC02（45）。部分应用不会被误判为任一完整配置。

## 兼容性与产物

执行控制前会检查前端版本和 OEM CPU 配置布局。
控件缺失、版本不支持、读回不可用或状态不匹配都会失败，
不会退化为只改变散热的主预设。
CPU／固件读回需要管理员权限。AWCC 保持其自身配置的自动化和网络行为。

其他硬件、语言、固件策略、游戏切换以及首次使用对话框不在当前验收范围内。
本实现不会安装驱动、直接写入自定义 CPU／GPU 值或自动重启。

二进制检查输出、硬件日志、本地配置和已编译探针保存在被 Git 忽略的 `.local` 目录，
不会随仓库上传。
