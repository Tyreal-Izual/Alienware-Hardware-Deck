# 联动预设控制

[English](PRESET-CONTROLS.md) | **简体中文**

## 验收状态

2026-11-06 已在审查过的 Aurora R16／i9-14900KF 和 AWCC 6.14.105.0 上通过实机验收。
合并测试验证了 Balanced/Off、Quiet/Off、Performance/OC01、
Performance 散热配合 OC Off、独立 OC 的双向切换，以及 Quiet 和 Balanced 下拒绝 OC01。
测试结束后恢复原来的 Performance/OC01 状态，PL1／PL2 均读回为 253 W。

OC02 后续验收也已通过：OC01 -> OC02 -> OC01、重复选择 OC02、OC02 -> Off、
Off -> OC02、OC02 -> Quiet/Off、Quiet／Balanced 下拒绝 OC02，
以及主 Performance 重新映射到 OC01。
测试恢复了原来的 P/OC01 状态，并在每 100 ms 一次的输入桌面采样中未发现可见 AWCC 进程窗口。
AWCC 原生日志佐证了 Overclock 02 激活和 BIOS 配置索引 2，且未要求重启。
证据：`.local/oc02/live-result.json`。

本轮审查时的自动化测试共通过 395 项：传感器／配置／电压 51 项、窗口状态 32 项、
网络 27 项、预设策略／校验 49 项、生命周期／读回 26 项、
显示 181 项、打包／worker 协议 29 项。
1080p 全屏在 100% 和 125% 缩放下无需滚动即可完整显示。

CPU 读回在执行已检查的 `RefreshActiveValuesFromHardwareAccess` 后，
读取 `GetAllControls().ActiveValue`。
该刷新操作只更新服务的读取缓存，不会提出或应用调参变更。
早期返回零值的标量 getter 未被使用。实机数据验证了 53 个必需控制值。

本地证据：`.local/awcc-sync/linked-presets-live.json`、
`cooling-policy-live.json` 和 `cold-start-result.json`。

OC02 可执行文件和视图已部署到项目根目录。
旧构建及对应视图／配置备份在 `.local/before-oc02`。
面板以管理员权限重新打开，并恢复保存的窗口设置。
只读界面检查确认数据实时更新、P/OC01 高亮正确、OC02 按钮可见且可用，也没有待处理请求。
最初的普通权限 UI 探针只能看到管理员窗口的标题栏；使用相同权限后检查成功。
证据：`.local/oc02/dashboard-smoke-admin.txt`。

## 控件

| 控件 | 散热目标 | OC 目标 |
| --- | --- | --- |
| 主 Quiet | Quiet | OEM Off |
| 主 Balanced | Balanced | OEM Off |
| 主 Performance | Performance | OEM OC01 |
| Cooling Q / B | Quiet / Balanced | 强制 OEM Off |
| Cooling P | Performance | 保留已验证的 Off / OC01 / OC02 |
| OC Off | 保留当前标准散热模式 | OEM Off |
| OC01 | 要求已选择 Performance 散热 | OEM OC01 |
| OC02 | 要求已选择 Performance 散热；仅手动选择 | OEM OC02 |

允许五种组合：Quiet/Off、Balanced/Off、Performance/Off、Performance/OC01 和 Performance/OC02。
主预设不会选择 OC02；主 Performance 始终回到 OC01。
Quiet／Balanced 散热下，界面和 worker 策略都会拒绝两个 OC 等级。
只有实际 CPU 和散热组合匹配时，联动主预设才会高亮；
Performance 散热配合 CPU Off 或 OC02 会显示为 Independent。
请求待处理期间所有控件锁定；重复请求已经验证的组合不会执行写入。

分项控件与主按钮共用同一套策略、事务、校验和结果路径，
没有另一套相互竞争的 CPU 调参实现。

## 应用路径

主预设调用 AWCC 原生无障碍预设控件，由其自身服务应用完整预设，
包括相应的 CPU 和散热操作。
独立的小型 worker 进程使面板和传感器采样保持响应，
并在同一 Windows 会话的多个 HardwareDeck 实例之间串行执行切换。

AWCC 关闭时，worker 在私有 Windows 桌面上启动已安装的前端，不会切换用户的输入桌面。
Windows job 对象负责管理临时前端，即使 worker 意外退出，也会清理该前端。
不会终止 Dell 服务。
实机检查显示，这种启动方式大约四秒后即可提供控件，输入桌面上未发现 AWCC 窗口。

用户已打开的 AWCC 前端会被复用并最小化，不会被终止。
它必须提供 Dashboard／Performance 页的预设控件。
其他页面、不可用控件、对话框或不支持的版本会使操作失败，
不会盲目发送鼠标／键盘输入或关闭提示框。
必要时，会先检查窗口所属进程，再仅将键盘焦点交回发起请求的 HardwareDeck 窗口。

OC02 通过私有桌面上的 AWCC 原生 Custom CPU 编辑器应用。
选择 OC02 前需关闭已有的 AWCC 前端；worker 不会在用户的编辑器中跳转或覆盖保存。
进入 Custom 可能应用其中保存的散热策略，因此 worker 会在选择和保存 OC2 前
重新建立并验证 Performance 散热。
它在 SAVE 前还会再次检查 P，随后校验实际 CPU 和散热状态。
AWCC 会将其保存为 Custom CPU 选项。
点击主预设时会退出 Custom，应用常规 Off/OC01 映射，并恢复原来的视图显示偏好。

Performance 散热配合 OC Off 时，先由 AWCC Balanced 建立默认 OC 设置，
确认 OC 后，再通过现有允许列表内的散热写入接口选择 Performance。
OC 生效期间选择更低散热档位，会调用相应的完整 AWCC 预设，并必须确认 CPU Off。
如果无法验证变更，且 CPU 状态未知或 OC 仍开启，worker 会尝试保持或恢复
Performance 散热，并报告失败。
AWCC 外部自动化仍可能覆盖手动组合；后续读回会更新界面状态。

## 散热功耗策略

BIOS 散热模式会独立于 OC 频率／电流设置改变 PL1。
本机实测参考值如下：

| 散热 | 长时上限（PL1） | 短时上限（PL2） |
| --- | --- | --- |
| Quiet | 170 W | 253 W |
| Balanced | 180 W | 253 W |
| Performance | 253 W | 253 W |

因此，Performance 散热配合 OC Off 会保留默认的优选 P 核 60x 目标倍率
和 307 A IccMax，但使用 253 W 散热功耗策略。
不能仅与 Off 配置文件中的 180 W PL1 字段比较。
OC 身份和散热功耗分别校验；功耗上限错误仍会阻止成功状态和联动预设高亮。

独立组合属于实时覆盖。
AWCC 可能仍显示用于建立 CPU Off 的标准预设，其游戏／电源自动化也可能覆盖手动散热选择。
HardwareDeck 显示的是实际组合读回结果。

## 校验

- CPU／OC：批量 XTU 读取包含优选／活动 P 核倍率、全部 E 核独立倍率、
  全部 16 个活动 E 核数量限制、IccMax、电压模式与电压偏移。
  OC 特征必须匹配本地 OEM Off/OC01/OC02 定义。
  控制 ID 缺失、无效或全零响应、自定义值和不匹配值都不能视为已验证的 OEM OC 预设。
- 散热：通过 WMI 读取实际固件散热模式，并依据上述实测策略校验 XTU PL1／PL2。
  这些检查合计覆盖 53 个控制值。
- 完成：请求后要求连续四次读回匹配。
  单纯的界面选择或请求设置不能决定成功；新的 worker 结果不会被更旧的传感器缓存快照覆盖。
- 采样：复用 XTU 连接，普通 CPU 预设读数缓存三秒。
  收到请求或观察到散热变化时强制刷新，避免新散热状态搭配旧 CPU 缓存。
  读回接口不暴露调参 setter、SDK 初始化、驱动安装或电压写入操作。

初始兼容范围是已审查的 R16／i9-14900KF OEM 布局和 AWCC 6.14.105.0。
CPU 与固件读回需要管理员权限。
其他机型、不支持的 AWCC 版本或无法验证的数据源会禁用或拒绝控制。

校验器检查运行时 CPU 目标和散热，不代表负载稳定性测试，也不覆盖所有固件参数。
它不会自动重启或接受 AWCC 警告对话框。
只读本地配置研究见 [AWCC-LOCAL-PROFILES.md](AWCC-LOCAL-PROFILES.zh-CN.md)。

关闭面板会停止采样并拒绝排队中的界面更新。
已经启动的预设 worker 仍可完成获准的操作与清理；结果管道关闭会被安静处理。
即使 worker 随后返回成功，超时仍然按失败处理。
启动失败只会通过保留的原生句柄终止该 worker 自己创建的挂起前端。
这些关闭和失败路径已有不写入硬件的回归测试。
