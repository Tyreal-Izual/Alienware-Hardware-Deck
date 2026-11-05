# HardwareDeck

独立的 Windows 原生硬件面板，面向 Alienware Aurora R16。可用于副屏常驻，与 [PulseDeck](https://github.com/Tyreal-Izual/PulseDeck) 分开运行。

## 启动

需要 Windows 10/11 x64、.NET Framework 4.8，以及相应的 AWCC/NVIDIA 驱动。

- 双击 **Start-HardwareDeck.cmd**：普通权限启动。首次运行会自动从源码编译。
- 双击 **Start-With-Sensors.cmd**：申请管理员权限，启用 AWCC 温度、风扇和散热模式控制。
- 支持置顶、切换显示器、F11 全屏，以及 1/2/5 秒刷新。关闭窗口即停止采集。
- 更新源码后，先关闭窗口，再运行 `powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1`。

没有云端账号、遥测上传、HTTP 服务或自动驱动安装。正常采样在内存中运行，不定时写快照，也不反复启动 GPU 查询进程。当前尚未采用后台服务架构，完整传感器启动仍需 UAC。

## 数据与边界

| 数据 | 来源与限制 |
| --- | --- |
| CPU 负载 / 内存 | Windows 系统接口 |
| CPU 频率 | Windows 性能计数器估算，不是单核瞬时峰值 |
| CPU 功耗 | Intel IPF/RAPL；启动预热并过滤异常值 |
| CPU 电压 | 仅读取已有兼容 WMI 电压源；没有可靠来源时显示“未提供” |
| GPU 指标 | 进程内 NVIDIA NVML；风扇值为驱动报告的百分比 |
| AWCC 温度 / RPM | 本机 WMI；需要管理员权限，其他机型未保证兼容 |
| 风扇关联 | `GetFanSensors` 子命令 2；关联码不等于物理安装位置 |

**模式控制仅在点击“应用”后执行。** Quiet、Balanced、Performance 必须同时通过本机 AWCC 配置及固件枚举验证，操作后读回确认。不提供自定义风扇曲线、电压或超频写入。AWCC 自动配置可能覆盖手动选择。

## 温度源配置

首次编译生成本机 `sensor-labels.json`，该文件不提交 Git。可参照 `sensor-labels.example.json`：

```json
{
  "cpuTemperatureSensor": "awcc.temp.1",
  "labels": {
    "awcc.temp.1": "CPU 温度（确认后命名）"
  }
}
```

指定 ID 缺失时不会换用别的温度。设为 null 时兼容首个温度的自动选择，并标明 CPU 身份尚未确认。旧版扁平名称映射仍支持。修改配置后重启；悬停风扇卡片可查看关联信息。

## 开发与验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Packaging.Tests.ps1
```

`Build.ps1 -OutputPath .local/build/HardwareDeck.exe` 会同时复制运行所需资源。
只读诊断导出：`HardwareDeck.exe --probe C:\path\snapshot.json`。
内部界面预览：`HardwareDeck.exe --preview C:\path\preview.png`。

`src/Sensors.cs`、`CpuTelemetry.cs` 和 `NvidiaReader.cs` 负责采集；
`SensorConfiguration.cs` 负责数据源选择；`App.cs` 与 `View.xaml` 负责界面。
测试不切换实际硬件模式。详见 [验证范围](VERIFICATION.md)。

MIT 许可。AWCC 协议映射参考 AlienFX Tools，原始许可见 [第三方声明](THIRD-PARTY-NOTICES.md)。
