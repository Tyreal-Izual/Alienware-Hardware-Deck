# HardwareDeck

[English](README.md) | **简体中文**

面向 Alienware Aurora R16 的独立原生 Windows 硬件监控面板，
适合长期显示在 1920 × 1080 的 22N1 等副屏上。

## 显示内容

- CPU 和 GPU 使用率、温度、频率，以及当前功耗与功耗上限。
- CPU 电压、内存使用量、风扇转速和其他 AWCC 温度传感器读数。
- 尺寸一致的 120 秒温度与网络上传／下载曲线，支持悬浮查看细节。
- 经过状态校验的 Quiet、Balanced、Performance 主预设，以及独立的散热和 OC 控制。

软件界面使用英文。主要读数采用大号粗体，便于从主屏幕上方的副屏查看。
HardwareDeck 不需要云账号，不提供上传服务，也不会自动安装驱动。
被调用的 AWCC 组件仍遵循其自身配置。

## 环境要求与启动

需要 Windows 10/11 x64、.NET Framework 4.8，以及已安装的相应 AWCC／NVIDIA 驱动。
完整预设控制已在搭载 i9-14900KF、RTX 4090 和 AWCC 6.14.105.0 的 Aurora R16 上验证。
其他硬件或 AWCC 版本可能出现读数不可用或控制按钮禁用的情况，
具体见[验证范围](VERIFICATION.zh-CN.md)。

1. 在正常 Windows 登录状态下运行 **Start-HardwareDeck.cmd**。
   首次使用会构建暂存包，并请求一次 UAC 确认来配置专用任务。
2. 配置完成后，使用该脚本、**Start-With-Sensors.cmd** 或 **HardwareDeck** 桌面快捷方式。
   传感器会直接启用，不再重复弹 UAC。任务使用同一账号，仅在其已登录的交互会话中运行。
3. 使用 **Manage-HardwareDeck.cmd Check / Repair / Uninstall** 检查、更新或移除此配置。
   修复／更新和卸载仍需要管理员确认。

任务启动 `%ProgramFiles%\HardwareDeck\<SID>` 下的固定受保护副本。
源码更新和传感器标签修改需要通过 **Repair** 更新该副本。
直接运行 EXE 仍继承启动者权限；为保留测试和诊断入口，manifest 保持 `asInvoker`。
完整约定、取消授权处理和显式普通模式启动见[管理员启动说明](STARTUP.zh-CN.md)。

## 显示与窗口设置

默认每 **1 秒**刷新，可选择 1／2／5 秒间隔。顶部还提供 **Next display**（切换屏幕）、
**Pin**（置顶）和 **Fullscreen**（全屏）。按 F11 切换全屏，按 Esc 退出全屏。
窗口采用深色原生边框和内嵌应用图标。

在已测试的 R16 传感器配置下，四张图表可完整显示在 Windows 缩放为 100% 或 125% 的
1080p 全屏窗口中。更高缩放或较小窗口可通过纵向滚动查看内容。
将鼠标悬停在读数上可查看来源和详情；顶部会提示数据过期或采样停止。

屏幕、窗口位置与大小、全屏／最大化和置顶状态保存在
`%LOCALAPPDATA%\HardwareDeck\window-state.json`。
同一 Windows 账号下的普通权限和管理员启动共用这些设置。
如果保存的屏幕已断开，程序会使用可用屏幕，并保留原来的首选屏幕，直到你主动移动或调整窗口。
若要重置位置，关闭程序后仅删除该设置文件即可。

## 性能与散热

| 主预设 | 散热 | CPU 预设 |
| --- | --- | --- |
| Quiet | Q | Off |
| Balanced | B | Off |
| Performance | P | OC01 |

独立的 **Cooling Q/B/P** 和 **OC Off/OC01/OC02** 按钮共用同一套切换保护与校验流程。
Q 和 B 会强制关闭 CPU OC。OC01 和 OC02 都要求 P 散热。
OC02 只能手动选择；主 Performance 按钮始终选择 OC01。
P 散热搭配 CPU Off 或 OC02 时，会显示为独立组合。

按钮根据实际读回状态高亮，请求执行期间所有按钮都会锁定。
AWCC 的原生流程在后台运行。主预设可复用并最小化已有的 AWCC 窗口；
**选择 OC02 前请先关闭 AWCC**，让其 CPU 编辑器在私有桌面上运行。
AWCC 的游戏或电源自动化可能覆盖手动组合。
不支持的版本、不可用的读回或状态不匹配都无法通过校验。

CPU 和 GPU 功耗都采用 `current / limit W` 格式。
CPU 上限是当前持续封装功耗上限 PL1，会随模式变化更新。
悬浮说明还会显示短时加速上限 PL2，因此瞬时功耗可能高于 PL1。
缺失或过期的上限数据显示为破折号。
在已测试机器上，Q／B／P 对应的 PL1 为 170／180／253 W，PL2 均为 253 W。
界面始终依据实际读回值显示。完整行为和兼容性限制见[预设控制](PRESET-CONTROLS.zh-CN.md)。

## 数据来源

| 指标 | 来源与含义 |
| --- | --- |
| CPU 使用率／内存 | Windows 系统 API；内存单位为 GiB |
| CPU 频率 | Windows 性能计数器估算值，并非单核瞬时峰值 |
| CPU 功耗／上限 | Intel IPF/RAPL 封装功耗；上限来自 XTU 当前 PL1 读回 |
| CPU 电压 | 已安装 Intel XTU 的电压读取接口；可回退到已有的兼容 WMI 提供程序 |
| GPU | NVIDIA NVML；GPU 风扇速度是驱动报告的百分比 |
| AWCC 温度／风扇 | 本地 WMI；风扇关联码无法确定风扇的实际物理位置 |
| 网络 | Windows 中已连接物理网卡的 64 位接口计数器 |

上传与下载曲线分别显示速率和近期峰值，自动采用 B/s、KB/s、MB/s 等单位
（1 KB = 1,000 字节）。统计包含局域网流量和协议开销，并非互联网测速。
启动、网卡变化或计数器重置后，需要新的采样间隔建立基线。
空闲流量显示零；数据源不可用时显示缺失读数。
历史记录保存在内存中，正常采样不会定期写入快照。

## 温度源配置

首次构建会根据 [sensor-labels.example.json](sensor-labels.example.json)
生成本地的 `sensor-labels.json`，该文件不会被 Git 跟踪：

```json
{
  "cpuTemperatureSensor": "awcc.temp.1",
  "labels": {
    "awcc.temp.1": "CPU temperature (confirm sensor identity first)"
  }
}
```

编辑后使用 **Manage-HardwareDeck.cmd Repair** 更新受保护副本。
显式普通模式启动会在重启后读取源码目录中的副本。指定的传感器 ID 缺失时，不会静默换用其他传感器。
将 ID 设为 null 会启用旧版的首个传感器选择方式，并标明 CPU 身份未经确认；
旧版扁平标签映射仍受支持。

## 开发与参考

| 文档 | 内容 |
| --- | --- |
| [管理员启动](STARTUP.zh-CN.md) | 一次配置、日常启动、检查、修复和卸载 |
| [开发指南](DEVELOPMENT.zh-CN.md) | 构建／测试命令、源码结构与执行路径 |
| [预设控制](PRESET-CONTROLS.zh-CN.md) | 模式组合、保护与读回规则 |
| [验证记录](VERIFICATION.zh-CN.md) | 自动化和实机检查及其限制 |
| [AWCC 集成](AWCC-INTEGRATION.zh-CN.md) | 后台控制与本地服务研究 |
| [OEM 配置审查](AWCC-LOCAL-PROFILES.zh-CN.md) | Off、OC01、OC02 的定义 |
| [更新日志](CHANGELOG.zh-CN.md) | 版本历史 |

采用 MIT 许可证。AWCC 协议映射改编自 AlienFX Tools；
详见[第三方声明](THIRD-PARTY-NOTICES.zh-CN.md)。
