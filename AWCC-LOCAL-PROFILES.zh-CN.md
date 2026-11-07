# 本地 AWCC 模式与 OEM OC 配置

[English](AWCC-LOCAL-PROFILES.md) | **简体中文**

> 后续验证，2026-11-06：已通过 AWCC 原生 Custom CPU 编辑器激活并验证 OC02。
> 实机 worker 确认两组 E 核目标均为 45，P 核／电压／电流目标正确，
> 散热为 Performance，功耗上限为 253/253 W。
> AWCC 日志记录了 Overclock 02 激活和 BIOS 配置索引 2，且无需重启。
> 主预设映射保持不变。详见[预设控制](PRESET-CONTROLS.zh-CN.md)和[验证记录](VERIFICATION.zh-CN.md)。
> 以下只读审查记录的是这次独立实机测试之前掌握的证据，
> 其中“未激活”等描述属于历史状态。

本次审查于 2026-11-06 进行，仅使用已安装的 AWCC 文件、WMI 元数据和已有本地服务日志。
目标机器为 Alienware Aurora R16／Intel Core i9-14900KF／RTX 4090，AWCC 6.14.105.0。
本次审查没有修改配置，也没有激活 OC02。

## 结论

- 本地 OEM CPU 配置恰好包含三项预定义配置：**Off**、**Overclock 01**、**Overclock 02**，
  没有定义 OC03。
- AWCC 超频功能启用时，Quiet 和 Balanced 选择 CPU Off，Performance 选择 CPU OC01；
  它们独立选择各自的散热配置。
- OC02 的 P 核频率字段、PL1、PL2 和 IccMax 与 OC01 相同，
  但将 32 个 E 核控制值从 44 提高到 45；保存的内存字段也有不同。
- 根据检查到的 AWCC 运行时能力响应，内存 OC 和 GPU OC 均不可用。
  对应服务返回成功时，可能实际上没有执行设置。
- 主预设、CPU OC 配置、内存配置和散热配置彼此独立。
  共用 GUID 或名称中包含 Performance，都不代表它们可以互换。

后续实现验证还实测到了 BIOS 功耗覆盖：
Quiet、Balanced、Performance 散热下的最终 PL1 分别为 170 W、180 W、253 W，PL2 为 253 W。
Performance 散热配合 OC Off 会保留默认 OC 倍率／电流上限，但 PL1 是 253 W。
因此，Off 文件中保存的 180 W 并不是通用的最终运行时上限。
最新结果见[已验证的控制说明](PRESET-CONTROLS.zh-CN.md)。

## 本地证据

已安装 AWCC 目录下的路径：

`Subsystems/Performance/SubAgent/SCSubAgent/OCControlService/`

| 文件 | 证据 |
| --- | --- |
| `OCControl.Service.Overclocking.Data/Config.json` | R16/14900KF 的 OEM CPU 配置、XTU 控制目标、频率表和自定义设置范围 |
| `OCControl.Service.Overclocking.Data/Profile.json` | 缓存的活动 CPU／GPU／内存 ID，以及保存的自定义配置 |
| `OCControl.Service.Thermal.Data/Profile.json` | Balanced／Performance／Quiet 固件散热配置和一项自定义配置 |
| `OCControl.Service.Overclocking.Logs/2026-11-06.log` | 之前已授权的 Off/OC01 切换、完整 XTU 提案、结果和运行时能力 |
| `OCControl.Service.Thermal.Logs/2026-11-06.log` | 对应的散热配置应用记录 |

性能预设请求单独记录在
`%ProgramData%/Dell/AWPerformance.SCSubAgent/AWPerformance.SCSubAgent.03.log`。
纯元数据／IL 检查覆盖了 `PerformanceTables`、`PerformanceControl`、
Intel CPU／内存调参器、NVIDIA 调参器和 `Alienware.BiosOcKnobs`。

OEM 配置版本为 1。检查时的 SHA-256：
`dd788dc4d8718787ef9184df4588e88b7855c3bdcf7fd59b0b4690db2e886b5e`。
本地机器可读的对比结果保存在被 Git 忽略的
`.local/awcc-sync/local-profiles-audit.json`。

## 模式选择与实际调用

以下映射适用于常规应用模式，且 AWCC 超频功能已启用：

| 主界面选择 | 预设枚举 | CPU 目标 | 逻辑 GPU 目标 | 内存目标 | 散热目标 |
| --- | --- | --- | --- | --- | --- |
| Quiet | Quiet = 1 | Off | Off；调参器跳过本机 GPU | Off | UsttQuiet, 163 / 0xA3 |
| Balanced | Normal = 2 | Off | Off；调参器跳过本机 GPU | Off | UsttBalanced, 160 / 0xA0 |
| Performance | Fast = 3 | OC01 | OC01；调参器跳过本机 GPU | Off | UsttPerformance, 161 / 0xA1 |

观察到的界面点击路径如下：

1. `PerformancePresetsView` 先接收项目点击，随后接收选择变化。
2. `PerformancePresetsViewModel.ApplySelectedProfile` 读取当前应用模式和活动游戏 GUID，
   调用 `SetPerformancePreset(gameId, preset)`。离开 Custom 时还会调用 `SetCustomMode(false)`。
3. 预设插件保存相关设置，并发出手动变更事件。
4. `PerformanceControl` 读取当前应用／游戏／OC 状态，分别解析 CPU、GPU、内存和散热目标配置。
5. 对常规自动／手动预设变更，将每个目标配置 ID 与当前 ID 比较。
   发生变化的配置依次按 CPU、GPU、内存、散热顺序应用；未变化的组件会跳过。
6. 更新重启原因状态并报告完成或失败。
   Setter 返回、界面选择和完整应用完成是不同事件。

在已观察到的系统上下文中，对应行为如下：

| 切换 | 假设其他配置状态已匹配时，预期执行的操作 |
| --- | --- |
| Performance 到 Balanced | CPU Off；逻辑 GPU Off；内存不变；散热 Balanced |
| Performance 到 Quiet | CPU Off；逻辑 GPU Off；内存不变；散热 Quiet |
| Balanced 或 Quiet 到 Performance | CPU OC01；逻辑 GPU OC01；内存不变；散热 Performance |
| Quiet 到 Balanced，或 Balanced 到 Quiet | CPU／GPU／内存 ID 已匹配，仅改变散热 |
| 点击已经选中的模式 | 前端通常直接返回，不再发送预设请求 |

前三类切换有之前授权测试的本地服务证据。
Quiet／Balanced 直接切换时跳过未变组件的结论来自代码中的 ID 对比，
并非本次审查新增的实机测试。
该流程不是原子硬件事务：错误可能使部分组件已经改变，
因此替代实现必须读回状态并处理部分失败。

本地代码中还发现以下依赖状态的行为：

- **超频关闭：**CPU、GPU 和内存配置均解析为 Off，散热仍跟随所选主预设。
  在此状态下，Performance 并不必然代表 OC01。
- **Custom：**CPU／GPU／内存选择来自保存的自定义 OC 模式，散热使用自定义配置。
  自定义数据变更事件会应用相应组件。
- **系统与游戏：**预设属于当前活动上下文。
  游戏／电源自动变化也进入同一配置解析流程；只写死系统 GUID 会遗漏这些状态变化。
- 通用契约／代码中存在 GKey、Stealth、EnergySaver 和 Turbo，
  但这并不能证明本机 R16 支持这些模式。
  本地缓存的标准散热配置为 Quiet、Balanced、Performance，另有 Custom。

## Off、OC01 与 OC02：完整存储差异

每项 OEM 配置都有 46 个 XTU 条目，ID 各不相同。

| 字段 | Off | OC01 | OC02 |
| --- | --- | --- | --- |
| CPU 启用 | 是 | 是 | 是 |
| CPU 频率字段 | 6.0 GHz | 6.1 GHz | 6.1 GHz |
| E 核倍率，ID 1500-1515 | 44 | 44 | 45 |
| 活动 E 核数量倍率，ID 4500-4515 | 44 | 44 | 45 |
| Turbo Boost Power Max，ID 48 | 180 W | 253 W | 253 W |
| Turbo Boost Short Power Max，ID 47 | 253 W | 253 W | 253 W |
| Processor Core IccMax，ID 102 | 307 A | 316 A | 316 A |
| Turbo 功耗时间窗口，ID 66 | 56 s | 56 s | 56 s |
| CPU 电压模式 | Auto | Auto | Auto |
| CPU 电压字段／偏移 | 0 / 0 | 0 / 0 | 0 / 0 |
| Intel Turbo Boost，ID 26 | 启用 | 启用 | 启用 |
| 保存的内存启用状态／索引 | False / 0 | False / 0 | True / 1 |
| 默认预定义配置 | 是 | 否 | 否 |

除身份／默认标记元数据外，Off 到 OC01 只改变三个保存值：
`cpu.frequency`、XTU ID 48 和 XTU ID 102。
其他保存的 CPU／XTU／内存值均相同。

OC01 到 OC02 改变 34 项保存设置：两个内存字段，以及上表中的 32 个 E 核控制值。
CPU 频率、电压、PL1、PL2、IccMax 和其他 XTU 值均与 OC01 相同。
全核和逐核频率表中的两项对应行都是 `[6.1, 6.1, 0]`，Off 对应行是 `[6.0, 6.0, 0]`。

这些是目标配置字段，不是持续运行的实测值。
Auto 电压下的数值零不代表 CPU 供电电压为零。
Off 关闭的是 OEM OC 预设，而非正常的 Intel Turbo Boost。

## Off 与 OC01 的实际 XTU 提案

已有的 2026-11-06 日志包含：

- **13:26:46.179** 激活 CPU Off，原始日志第 2021 行。
- **13:27:18.179** 激活 CPU OC01，原始日志第 2423 行。

每个 `Proposal` 包含 **142 个唯一控制 ID**。
其中以下六项不同，其他 136 项数值相同。
为避免重复计数，对比未纳入后续的 `ProposalDelta` 块。

| XTU 控制 | Off 目标 | OC01 目标 |
| --- | --- | --- |
| 29：1 Active Performance Core | 60 | 61 |
| 30：2 Active Performance Cores | 60 | 61 |
| 120：Performance Core 4，优选核 | 60 | 61 |
| 121：Performance Core 5，优选核 | 60 | 61 |
| 48：Turbo Boost Power Max | 180 W | 253 W |
| 102：Processor Core IccMax | 307 A | 316 A |

两份提案中，3-8 个活动 P 核的倍率均保持 57。
其他独立 P 核目标也保持 57，E 核倍率控制保持 44。
因此，6.1 GHz 字段不是全核 6.1 GHz 目标。
优选核索引仅对应这次采集的机器。
这些是服务报告成功时提交的控制目标，并非每项控制的独立硬件读回或稳定性测试。

本地 Intel 调参器的执行顺序为：

`DiscardChanges -> build voltage/frequency/TVB/TDP proposals -> ProposeChange -> ApplyChanges -> BIOS OC operations`

BIOS 操作包括受支持时的 `BiosOcKnobs.SetVoltageOffset`，以及
`BiosOcKnobs.ApplyProfile`。
已有日志显示 Off 对应等级 0，OC01 对应等级 1。
这也说明，仅写入一个散热 ID 或一个 BIOS OC 等级，
尚未被证明等价于 AWCC 的完整 CPU 流程。

## OC02 的存在能够和不能够证明什么

OC02 是本机 OEM 配置中的真实条目，也是已安装服务契约中的明确模式。
代码包含选择第三项 CPU 配置的路径。
上述常规 Quiet／Balanced／Performance 映射不会选择它。

对日期从 2026-01-30 到 2026-11-06 的 **209 份已有 OCControl 超频日志**
进行搜索后，没有发现完全匹配的 `ActivateCpuProfile(Overclock 02)` 条目。
本次审查也没有激活 OC02、测试稳定性，或确认当前高级界面是否将其作为可用选项提供。

检查到的运行时系统信息响应如下：

- CPU：`overclockable: true`。
- AWCC 能力模型中的 RTX 4090：`overclockable: false`。
- `memory_overclockable: false`，`num_memory_profiles: 0`。

内存 OC 不可用时，`IntelCpuTuner.ActivateMemProfile` 会直接返回成功而不修改内存。
此外，其只读预定义配置路径使用当前的 `MemoryOCControl.ActiveProfile`；
保存的内存索引用于非只读的自定义配置。
因此，不能仅凭 OC02 保存的 `enabled: true, index: 1`
就断言选择 CPU OC02 会在本机应用 XMP1。
CPU 和内存应用路径彼此独立。应用 BIOS 等级 2 可能产生的固件副作用在本次审查时仍未测试。

同样，当 GPU 能力为 false 时，NVIDIA 调参器记录所选配置名称后，
会在执行调参前直接返回成功。
缓存中的逻辑 GPU OC01 状态不能证明 GPU 频率提高。

## 区分不同标识符

| CPU 配置 | OverclockMode 枚举 | BIOS OC 等级 | 配置 GUID |
| --- | --- | --- | --- |
| Off | OC0 = 1 | 0 | `92BA744B-4CF8-4281-A8DE-F8F1D3524D8C` |
| OC01 | OC1 = 2 | 1 | `F6E1318B-9DF4-4557-9D70-36586465FCD2` |
| OC02 | OC2 = 3 | 标准配置索引映射中的 2；本次审查未激活 | `8F6E0D5E-E1FA-4518-8819-C04B8F7E3048` |

散热服务将这些 GUID 分别复用于 **Balanced、Performance 和 Quiet**。
尤其需要区分：**Quiet 散热与 CPU OC02 虽共用一个 GUID，却是不同服务中的不同配置**。
Quiet 选择 CPU Off，而不是 CPU OC02。

检查到的 OverclockMode 契约还包括 UserDefined 和 NoChange，没有 OC03。
更多散热 GUID 或更大的自定义配置容量，并不能证明还存在其他 CPU OC 等级。

## 验证范围

- 重新读取了本地配置和代码；本次审查未使用外部研究资料。
- 对比了每项保存的 XTU 条目及两份各含 142 个控制目标的运行时提案，
  校验了总数、唯一 ID 和六项运行时差异。
- 检查了当前缓存的 CPU OC01、逻辑 GPU OC01、内存 Off 和 Performance 散热状态。
  缓存状态并不等于独立硬件读回。
- 没有调用调参 setter、修改 BIOS、运行压力测试或选择 OC02。
  本次审查没有改变正式 HardwareDeck 按钮。
