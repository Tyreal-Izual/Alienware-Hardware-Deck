# HardwareDeck

An independent native Windows hardware dashboard for the Alienware Aurora R16.
Designed for an always-visible secondary display, running separately from [PulseDeck](https://github.com/Tyreal-Izual/PulseDeck).

## Launch

Requires Windows 10/11 x64, .NET Framework 4.8, and the appropriate AWCC/NVIDIA drivers.

- **Start-HardwareDeck.cmd** starts with standard permissions and builds from source if the executable is missing.
- **Start-With-Sensors.cmd** requests administrator access for AWCC temperature, fan RPM, CPU voltage, and verified CPU/cooling presets.
- Refresh defaults to **1 second**; the header offers 1/2/5-second intervals, pinning, display switching, and fullscreen (F11 / Esc).
- Close the window to stop sampling. After source updates, close the app and run `powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1`.

The English interface is optimized for a **1920 x 1080 display such as the 22N1**.
It respects Windows DPI scaling, uses a compact layout when vertical space is limited,
and keeps status information beside the title. Temperature and network history rows have
matching dimensions and bounded heights (up to 220 logical pixels, or 158 in compact mode).
The four-chart dashboard fits a 1080p fullscreen display at 100% and 125% scaling with the
R16 sensor set. Larger scaling and smaller windows use vertical scrolling to keep plots
readable. Truncated source
details are available on hover. Memory values use GiB. A header indicator flags stale data.
Primary readings use bold weights and bright text for easier scanning on an elevated
secondary display; labels and chart ticks remain lighter to keep a clear visual hierarchy.

Windowed mode uses a dark native title bar and a subtle matching border on supported
Windows versions, retaining Windows caption buttons, resizing, and snapping. The application
icon is embedded for the title bar, taskbar, and executable; no separate runtime icon file
is required. System high-contrast colors take precedence over the custom frame palette.

HardwareDeck itself has no cloud accounts, telemetry uploads, HTTP services, or automatic
driver installs. Invoked AWCC components retain their own configured network, update,
and telemetry behavior.
Normal sampling stays in memory without periodic snapshots or repeated GPU helper processes.
Sensor tiles and brushes are reused between refreshes. Full sensor access still requires UAC;
the app does not yet use a separate privileged service.

## Window placement

HardwareDeck remembers the monitor, normal-window position and size, fullscreen/maximized
state, and pin setting. Preferences are saved to
`%LOCALAPPDATA%\HardwareDeck\window-state.json` after placement changes settle and when
the window closes. Normal and administrator launches under the same Windows account
share this file; enabling sensors saves placement before restarting.

Monitor identity takes precedence over Windows display numbering. Position and size are
stored relative to the display's working area in logical units, then adapted to its
current DPI and available space. If the preferred monitor is disconnected, the window
opens on an available display and retains the missing monitor preference until you
deliberately move/resize the window or choose Next display. Invalid settings fall back to
visible defaults. Diagnostic probes and preview exports do not change these preferences.
Exiting fullscreen follows the current monitor, including moves made through Windows.
To reset placement, close every HardwareDeck window and remove only `window-state.json`
from the user-data folder above; the next launch uses visible defaults.

## Sources and limitations

| Metric | Source and limitations |
| --- | --- |
| CPU load / memory | Windows system APIs |
| CPU frequency | Windows performance-counter estimate, not an instantaneous per-core peak |
| CPU power / limit | Intel IPF/RAPL package power and the current sustained (PL1) limit from XTU active readback; limit requires administrator access |
| CPU voltage | The installed Intel XTU service's voltage getter, also used by AWCC on this R16; administrator access required. Existing compatible WMI sources are a fallback. |
| GPU metrics | In-process NVIDIA NVML; fan speed is a driver-reported percentage |
| Network upload / download | Windows IP Helper 64-bit interface counters; combined traffic of connected physical adapters |
| AWCC temperature / RPM | Local WMI; administrator access required; other models are not guaranteed |
| Fan association | `GetFanSensors` subcommand 2; association codes do not identify physical fan positions |

**Quiet, Balanced, and Performance are linked CPU/cooling presets.** The compact
Cooling Q/B/P and OC Off/OC01/OC02 controls share the same transaction path. Quiet and
Balanced cooling force CPU Off; OC01 and OC02 are unavailable unless Performance cooling is
selected. OC02 is manual-only: main Performance always applies OC01, while Quiet and
Balanced apply Off. Performance cooling with CPU Off or OC02 is an Independent combination. Only
verified combinations receive a current highlight; requests lock all controls while
application and readback are pending.

The implementation delegates full presets to AWCC's own controls and
uses an isolated Windows desktop for a frontend cold start. Existing AWCC is reused
and minimized. CPU state is checked through XTU active values and cooling through
firmware WMI readback. Missing, stale or inconsistent data blocks verification.
This backend targets the audited R16/i9-14900KF and AWCC 6.14.105.0; raw voltage
and custom fan curves are not exposed. OC02 uses AWCC's native Custom CPU editor;
close an existing AWCC frontend before selecting it so the editor can run privately. AWCC automation may override manual
component combinations.

The full live switch matrix and OC interlock passed on the audited machine. Cooling
also sets a BIOS power policy: Quiet/Balanced/Performance use 170/180/253 W PL1,
respectively, with 253 W PL2. Performance cooling with OC Off is therefore a valid
independent combination at 253 W; OC identity and cooling power are checked separately.

CPU package power and GPU power show `current / limit W`, with the unit beside the
values. CPU limits follow actual PL1 readback, including immediately after a verified
mode change. The tooltip identifies PL1 and shows the short boost limit (PL2);
brief package-power readings can exceed the sustained limit. Missing or expired
CPU limit data displays an em dash.
See [preset controls and validation](PRESET-CONTROLS.md) for scope and limitations.

See the [local profile audit](AWCC-LOCAL-PROFILES.md) for Off/OC01/OC02 definitions and
[AWCC integration research](AWCC-INTEGRATION.md) for the original call-path evidence.

Voltage sampling uses a read-only local named-pipe contract containing only
`GetCoreVoltageValue`. It does not initialize the full tuning SDK, install drivers, start
services, or change voltage settings. The XTU connection is reused, requests have bounded
timeouts, and failures back off for 60 seconds. Hover over CPU voltage for its source or
failure reason. The private service contract may change with future AWCC/XTU versions;
unsupported or absent sources remain Unavailable rather than showing a substitute value.

## Network throughput

Download and upload each show the current transfer rate, a 120-second history, and the
recent peak. Hover over a plot for the sampled rate and over its title for adapter details.
Units automatically switch between B/s, KB/s, MB/s, and higher decimal byte-per-second
units (1 KB = 1,000 bytes). Each direction has its own automatically scaled vertical axis.

The reader samples local 64-bit counters with a monotonic clock and uses the actual elapsed
time, including when the refresh interval changes. It combines interfaces Windows reports
as connected hardware adapters, excluding filter, loopback, tunnel, and non-hardware
interfaces to avoid adding a VPN or virtual-switch counter to the same physical traffic.
These are interface rates, including LAN traffic and protocol overhead, not an internet
speed test or a measurement of individual applications. No packets are captured, no
traffic is generated for measurement, and no extra administrator access is required.

The first interval, adapter changes, counter resets, and long sampling gaps show a sampling
state until a fresh baseline is available. Idle links show zero; missing adapters or failed
queries show unavailable. History stays in memory and normal sampling does not write logs.

## Temperature-source configuration

The first build creates a local, Git-ignored `sensor-labels.json`. See `sensor-labels.example.json`:

```json
{
  "cpuTemperatureSensor": "awcc.temp.1",
  "labels": {
    "awcc.temp.1": "CPU temperature (confirm sensor identity first)"
  }
}
```

A missing selected ID is never silently replaced with another temperature source.
Setting the ID to null retains legacy first-sensor selection and labels its CPU identity
as unconfirmed. Legacy flat label maps remain supported. Restart after configuration
changes; hover over a fan tile to inspect its association information.

## Development and validation

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Packaging.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Display.Tests.ps1
```

`Build.ps1 -OutputPath .local/build/HardwareDeck.exe` also copies runtime resources.
Read-only diagnostics: `HardwareDeck.exe --probe C:\path\snapshot.json`.
Live preview export: `HardwareDeck.exe --preview C:\path\preview.png`.
Display tests render fixture previews to `.local/display-tests/renders` without collecting hardware data.
The icon source is `assets/HardwareDeck.svg`; regenerate the multi-size Windows icon with
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Icon.ps1`.

`src/Sensors.cs`, `CpuTelemetry.cs`, `XtuVoltageReader.cs`, `NvidiaReader.cs`, and `NetworkReader.cs` collect data;
`PresetControl.cs`, `CpuPresetReader.cs`, and `AwccPresetBridge.cs` own linked control, verification, and background AWCC access;
`SensorConfiguration.cs` selects the source; `App.cs` and `View.xaml` implement the UI.
`WindowPreferences.cs` and `WindowPlacementSession.cs` manage per-user placement and DPI
restoration; `WindowAppearance.cs` owns the native frame palette and embedded window icon.
Tests do not change actual hardware modes. See [validation scope](VERIFICATION.md).
See [the changelog](CHANGELOG.md) for network history, linked presets, readability, and review fixes.

MIT licensed. AWCC protocol mappings are adapted from AlienFX Tools; see [third-party notices](THIRD-PARTY-NOTICES.md).
