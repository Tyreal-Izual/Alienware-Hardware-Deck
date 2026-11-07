# HardwareDeck

A standalone native Windows hardware dashboard for the Alienware Aurora R16,
made for an always-visible secondary display such as a 1920 × 1080 22N1.

## What it shows

- CPU and GPU utilization, temperature, frequency, and power with the current limit.
- CPU voltage, memory usage, fan RPM, and additional AWCC temperature sensors.
- Matching 120-second temperature and network upload/download charts with hover details.
- Verified Quiet, Balanced, and Performance presets, plus independent cooling and OC controls.

The interface is entirely in English. Large, bold readings are designed to be visible
on a display above the main monitor. HardwareDeck has no cloud account, upload service,
or automatic driver installer. AWCC components retain their own configured behavior.

## Requirements and launch

Windows 10/11 x64, .NET Framework 4.8, and the appropriate installed AWCC/NVIDIA drivers
are required. Full preset control is validated on the Aurora R16 with an i9-14900KF,
RTX 4090, and AWCC 6.14.105.0. Other hardware or AWCC versions may have unavailable
readings or disabled controls; see [validation scope](VERIFICATION.md).

1. Run **Start-HardwareDeck.cmd** for standard-permission monitoring. The first launch
   builds the application if its executable is missing.
2. Run **Start-With-Sensors.cmd**, or select **Enable sensors** in the header, for AWCC
   temperatures, fan RPM, CPU voltage, CPU power-limit readback, and preset controls.
   Windows requests administrator confirmation.
3. Close the window to stop sampling. After updating source files, close the app and
   rebuild with `powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1`.

## Display and window settings

Refresh defaults to **1 second**, with 1/2/5-second choices. The header also provides
**Next display**, **Pin**, and **Fullscreen**. Press F11 to toggle fullscreen or Esc
to leave it. The window uses a dark native frame and an embedded application icon.

The four-chart dashboard fits 1080p fullscreen at 100% and 125% Windows scaling with
the tested R16 sensor set. Higher scaling and smaller windows allow vertical scrolling.
Hover over readings for sources and details; the header flags stale or stopped data.

The monitor, window position/size, fullscreen/maximized state, and pin setting are saved
in `%LOCALAPPDATA%\HardwareDeck\window-state.json`.
Standard and administrator launches under the same Windows account share these settings.
If the saved monitor is disconnected, the window uses an available display and retains
the preferred monitor until you deliberately move or resize it. To reset placement,
close the app and remove only that settings file.

## Performance and cooling

| Main preset | Cooling | CPU preset |
| --- | --- | --- |
| Quiet | Q | Off |
| Balanced | B | Off |
| Performance | P | OC01 |

Independent **Cooling Q/B/P** and **OC Off/OC01/OC02** buttons use the same guarded
switching and verification path. Q and B force CPU Off. OC01 and OC02 require P cooling.
OC02 is manual-only; the main Performance button always selects OC01. P cooling with
CPU Off or OC02 is shown as an independent combination.

Controls highlight actual readback, and all buttons lock while a request is pending.
AWCC's original workflow runs in the background. Main presets can reuse and minimize an
existing AWCC window; **close AWCC before choosing OC02** so its CPU editor can run on a
private desktop. AWCC game or power automation can override manual combinations.
Unsupported versions, unavailable readback, and mismatches prevent verification.

Both CPU and GPU power use `current / limit W`. The CPU limit is the current sustained
package limit, PL1, and updates after mode changes. The tooltip also shows the short
boost limit, PL2, so a brief power reading can exceed PL1. Missing or expired limit data
shows an em dash. On the tested machine, Q/B/P use 170/180/253 W PL1 and 253 W PL2.
The display always follows actual readback. See [preset controls](PRESET-CONTROLS.md)
for the full behavior and compatibility limits.

## Data sources

| Metric | Source and interpretation |
| --- | --- |
| CPU utilization / memory | Windows system APIs; memory is shown in GiB |
| CPU frequency | Windows performance-counter estimate, not an instantaneous per-core peak |
| CPU power / limit | Intel IPF/RAPL package power; XTU active PL1 readback for the limit |
| CPU voltage | Installed Intel XTU voltage getter; compatible existing WMI providers are a fallback |
| GPU | NVIDIA NVML; GPU fan speed is a driver-reported percentage |
| AWCC temperatures / fans | Local WMI; fan association codes do not identify physical fan positions |
| Network | Windows 64-bit interface counters for connected physical adapters |

Download and upload charts show separate rates and recent peaks, with automatic B/s,
KB/s, and MB/s units (1 KB = 1,000 bytes). They include LAN traffic and protocol overhead;
they are not an internet speed test. Startup, adapter changes, and counter resets need
a fresh sampling interval. Idle traffic shows zero; unavailable sources show a missing
reading. History stays in memory, and normal sampling does not write periodic snapshots.

## Temperature-source configuration

The first build creates a local, Git-ignored `sensor-labels.json` from
[sensor-labels.example.json](sensor-labels.example.json):

```json
{
  "cpuTemperatureSensor": "awcc.temp.1",
  "labels": {
    "awcc.temp.1": "CPU temperature (confirm sensor identity first)"
  }
}
```

Restart after editing this file. A missing selected ID is never silently replaced.
Setting the ID to null uses legacy first-sensor selection with an unconfirmed CPU
identity; legacy flat label maps are also supported.

## Development and reference

| Document | Contents |
| --- | --- |
| [Development guide](DEVELOPMENT.md) | Build/test commands, source map, and execution paths |
| [Preset controls](PRESET-CONTROLS.md) | Mode combinations, guards, and readback rules |
| [Validation](VERIFICATION.md) | Automated and live checks, with their limits |
| [AWCC integration](AWCC-INTEGRATION.md) | Background control and local service research |
| [OEM profile audit](AWCC-LOCAL-PROFILES.md) | Off, OC01, and OC02 definitions |
| [Changelog](CHANGELOG.md) | Version history |

MIT licensed. AWCC protocol mappings are adapted from AlienFX Tools;
see [third-party notices](THIRD-PARTY-NOTICES.md).
