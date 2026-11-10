# 日常管理员启动

[English](STARTUP.md) | **简体中文**

## 首次配置

在正常 Windows 登录状态下运行 **Start-HardwareDeck.cmd**。
启动器先构建暂存安装包，再请求一次 UAC 确认，注册专用的按需启动任务。
配置使用同一个 Windows 账号，该账号必须属于管理员。
如果提供另一个管理员账号的凭据，配置会拒绝执行，而不会切换用户配置。

获准的应用及其视图／配置安装在 `%ProgramFiles%\HardwareDeck\<SID>` 下。
安装目录由 Administrators 和 SYSTEM 拥有和管理，当前用户仅有读取／执行权限。
任务不会指向可写的源码目录、命令解释器或由调用者指定的可执行文件。

配置时会正常关闭已有面板，保存窗口偏好；禁用原任务前会先检查后台操作，
后台操作必须先完成。
成功验证后，原启动器创建 **HardwareDeck** 桌面快捷方式并启动任务。
桌面文件和源码目录里的 EXE 由原启动器更新，不在管理员安装进程中写入。

## 日常启动

使用桌面快捷方式、**Start-HardwareDeck.cmd** 或 **Start-With-Sensors.cmd**。
配置完成后，这些入口调用同一个固定任务，不再重复弹 UAC，面板直接启用传感器。
Sensors 脚本保留为兼容入口。任务没有自动登录／开机触发器，仅按需运行。

任务使用当前账号、`InteractiveToken` 和 `HighestAvailable`，
并在发起者的交互会话中启动；不保存密码，也不使用 SYSTEM 身份运行。
任务只有一个固定 EXE 动作，参数为空，工作目录受保护。
不接受 `$(Arg0)` 替换或任意诊断参数。

调度器使用 `IgnoreNew` 忽略重复启动。
按用户和登录会话隔离的互斥锁，还会阻止直接启动与任务启动产生重复面板窗口。
诊断、预览和预设 worker 保留各自入口。
原有 Enable sensors 提权交接期间，会等待普通窗口正常关闭。

屏幕身份、窗口大小、全屏／最大化及置顶仍使用同一用户的
`%LOCALAPPDATA%\HardwareDeck\window-state.json`。
现有 CPU／OC／散热保护和硬件状态读回继续生效。

## 检查、修复、更新与卸载

在源码目录的普通终端运行以下命令；也可双击 **Manage-HardwareDeck.cmd** 查看检查结果：

```text
Manage-HardwareDeck.cmd Check
Manage-HardwareDeck.cmd Repair
Manage-HardwareDeck.cmd Uninstall
```

- **Check** 只读，报告任务身份、状态、上次结果、固定动作、安装位置和验证问题。
  健康任务必须具备预期账号、交互／最高权限标志、重复策略、动作和权限。
- **Repair** 会再次请求 UAC。从源码目录执行时，会重新构建暂存包、正常关闭旧面板，
  更新受保护安装目录和传感器标签，恢复任务定义并重建快捷方式。
  源码更新需要这一步显式操作；日常启动不会静默提权执行新的源码目录代码。
- **Uninstall** 请求 UAC，仅移除当前用户已识别的专用任务和受管理快捷方式。
  应用文件和窗口偏好会保留，不删除其他任务、Dell 服务或其他用户的注册项。
  被修改过或与本程序无关的同名快捷方式会保留，不覆盖或删除。

任务名称为 `\HardwareDeck\Dashboard-<SID>`。
修复／删除现有任务前会检查管理标记。
所有权异常、已安装代码可被普通用户写入或路径包含重解析点时，操作会失败，不会放宽保护。

编辑源码目录中被 Git 忽略的 `sensor-labels.json` 后，使用 **Repair** 部署。
安装目录中的副本受保护。开发时需要继承当前权限启动，可使用：

```text
Start-HardwareDeck.cmd -Basic
```

## 失败处理与直接运行 EXE

取消 UAC 会停止配置，不注册任务，也不回退到未经验证的管理员启动。
已有任务被禁用、改动、缺少文件或出现其他校验问题时，不会运行。
启动器会要求明确执行 **Check**／**Repair**，而不会反复弹 UAC。
注册项确实不存在时才走首次配置。修复中断后，任务可能保持禁用，直到修复成功。

Manifest 保持 `asInvoker`。直接双击 EXE 会继承启动者权限，可能显示 **Enable sensors**；
该按钮仍会通过 UAC 提权。直接 EXE 不是免重复确认入口。
仅将 manifest 改为 `requireAdministrator`，既不能实现此计划任务启动行为，
也会不必要地提权测试和诊断入口。

## 验证与平台依据

离线检查覆盖首次／重复启动决策、取消 UAC、无效任务动作／主体／设置、
任务／文件权限以及单实例交接，不注册任务或改变硬件模式。
实机验收记录见 [VERIFICATION.md](VERIFICATION.zh-CN.md)。

Windows 机制说明：[任务安全与运行级别](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks)、
[交互式注册](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertaskdefinition)、
[指定会话的 RunEx](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-runex)、
[重复实例策略](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-multipleinstances)。
