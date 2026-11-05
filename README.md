# HardwareDeck

An independent native Windows hardware dashboard for the Alienware Aurora R16.
Designed for an always-visible secondary display, running separately from [PulseDeck](https://github.com/Tyreal-Izual/PulseDeck).

## Launch

Requires Windows 10/11 x64, .NET Framework 4.8, and the appropriate AWCC/NVIDIA drivers.

- **Start-HardwareDeck.cmd** starts with standard permissions and builds from source if the executable is missing.
- **Start-With-Sensors.cmd** requests administrator access for AWCC temperature, fan RPM, CPU voltage, and thermal presets.
- Refresh defaults to **1 second**; the header offers 1/2/5-second intervals, pinning, display switching, and fullscreen (F11 / Esc).
- Close the window to stop sampling. After source updates, close the app and run `powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1`.

The English interface is optimized for a **1920 x 1080 display such as the 22N1**.
It respects Windows DPI scaling, uses a compact layout when vertical space is limited,
and keeps status information beside the title. Temperature history panels have a bounded
height (up to 220 logical pixels, or 158 in compact mode), so fullscreen never stretches the
plots to fill the remaining screen. The main dashboard fits at 100%, 125%,
and 150% scaling with the R16 sensor set; smaller windows can scroll. Truncated source
details are available on hover. Memory values use GiB. A header indicator flags stale data.

Windowed mode uses a dark native title bar and a subtle matching border on supported
Windows versions, retaining Windows caption buttons, resizing, and snapping. The application
icon is embedded for the title bar, taskbar, and executable; no separate runtime icon file
is required. System high-contrast colors take precedence over the custom frame palette.

There are no cloud accounts, telemetry uploads, HTTP services, or automatic driver installs.
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
| CPU power | Intel IPF/RAPL, with warmup and invalid-value filtering |
| CPU voltage | The installed Intel XTU service's voltage getter, also used by AWCC on this R16; administrator access required. Existing compatible WMI sources are a fallback. |
| GPU metrics | In-process NVIDIA NVML; fan speed is a driver-reported percentage |
| AWCC temperature / RPM | Local WMI; administrator access required; other models are not guaranteed |
| Fan association | `GetFanSensors` subcommand 2; association codes do not identify physical fan positions |

**Thermal modes change only when you click Apply.** Quiet, Balanced, and Performance must
match both local AWCC configuration and firmware enumeration. Changes are confirmed by
readback. Custom fan curves, voltage changes, and overclock writes are not supported.
AWCC automation may override a manually selected preset.

Voltage sampling uses a read-only local named-pipe contract containing only
`GetCoreVoltageValue`. It does not initialize the full tuning SDK, install drivers, start
services, or change voltage settings. The XTU connection is reused, requests have bounded
timeouts, and failures back off for 60 seconds. Hover over CPU voltage for its source or
failure reason. The private service contract may change with future AWCC/XTU versions;
unsupported or absent sources remain Unavailable rather than showing a substitute value.

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

`src/Sensors.cs`, `CpuTelemetry.cs`, `XtuVoltageReader.cs`, and `NvidiaReader.cs` collect data;
`SensorConfiguration.cs` selects the source; `App.cs` and `View.xaml` implement the UI.
`WindowPreferences.cs` and `WindowPlacementSession.cs` manage per-user placement and DPI
restoration; `WindowAppearance.cs` owns the native frame palette and embedded window icon.
Tests do not change actual hardware modes. See [validation scope](VERIFICATION.md).
See [the changelog](CHANGELOG.md) for the current window-persistence and appearance update.

MIT licensed. AWCC protocol mappings are adapted from AlienFX Tools; see [third-party notices](THIRD-PARTY-NOTICES.md).
