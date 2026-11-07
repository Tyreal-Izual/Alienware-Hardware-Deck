# Development

**English** | [简体中文](DEVELOPMENT.zh-CN.md)

HardwareDeck is a single Windows .NET Framework 4.8 executable with a runtime-loaded
WPF view. Building requires the Windows Framework C# compiler; no NuGet restore or
.NET SDK is needed. It is developed and released as a standalone repository.

## Build and validate

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -OutputPath .local/build/HardwareDeck.exe
powershell -NoProfile -ExecutionPolicy Bypass -File Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Packaging.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Display.Tests.ps1
```

Use `Build.ps1` without `-OutputPath` to update the root executable after closing the
running dashboard. The build copies the view and runtime configuration, creates sensor
labels only when missing, and embeds the application icon. Local sensor preferences
are preserved. Launch scripts build only when needed; source edits require a rebuild.

[scripts/Compiler.ps1](scripts/Compiler.ps1) owns the compiler location, framework
references, and production source discovery used by the application and test scripts.
All production C# files directly in `src` are compiled together. The core test executable selects
its own entry point, so tests can access internal classes without maintaining a second
list of production files. Display tests use the actual built application assembly.

| Check | Scope |
| --- | --- |
| `Test.ps1` | Sensor contracts, window state, network rates, preset policy, readback, worker lifecycle |
| `tests/Packaging.Tests.ps1` | First build, resource copying, launcher behavior, invalid worker requests |
| `tests/Display.Tests.ps1` | Actual WPF layout, power/OC states, stale data, and 1080p scaling |

Automated tests use fixtures or fake hardware backends and do not change hardware modes.
Display previews are written to `.local/display-tests/renders`. CI runs the same commands;
it has no Alienware hardware. See [VERIFICATION.md](VERIFICATION.md) for live acceptance.

Read-only diagnostics and preview exports:

```powershell
.\HardwareDeck.exe --probe C:\path\snapshot.json
.\HardwareDeck.exe --preview C:\path\preview.png
```

The probe collects a snapshot and exits. Preview captures the live dashboard, rather
than fixture data. Neither changes saved window placement. Full sensors still require
administrator access. Diagnostics contain machine information and stay local by default.

## Source map

| Area | Files and responsibilities |
| --- | --- |
| Entry point | [Program.cs](src/Program.cs): dashboard, probe/preview, and internal worker command routing |
| Dashboard | [DeckWindow.cs](src/DeckWindow.cs): window lifecycle, sampling, layout, and reading updates |
| UI sections | [DeckWindow.Presets.cs](src/DeckWindow.Presets.cs): control states and requests; [DeckWindow.History.cs](src/DeckWindow.History.cs): charts; [DeckWindow.Tiles.cs](src/DeckWindow.Tiles.cs): reusable sensor tiles |
| View | [View.xaml](View.xaml): English labels, layout, and styles |
| Sampling | [Collector.cs](src/Collector.cs): coordinates readers; [Snapshot.cs](src/Snapshot.cs): shared data and explicit snapshot serialization |
| Sensors | [SystemReader.cs](src/SystemReader.cs), [CpuTelemetry.cs](src/CpuTelemetry.cs), [XtuVoltageReader.cs](src/XtuVoltageReader.cs), [NvidiaReader.cs](src/NvidiaReader.cs), [NetworkReader.cs](src/NetworkReader.cs) |
| Firmware | [AwccReader.cs](src/AwccReader.cs): WMI temperatures, fans, and guarded cooling writes; [ThermalModes.cs](src/ThermalModes.cs): discovered preset metadata |
| Configuration | [SensorConfiguration.cs](src/SensorConfiguration.cs): sensor labels and CPU-temperature selection |
| Preset rules | [PresetControl.cs](src/PresetControl.cs): standard mode constants/order/names, target resolution, and verification transaction |
| Preset execution | [PresetBackend.cs](src/PresetBackend.cs): real readers and actions; [AwccPresetBridge.cs](src/AwccPresetBridge.cs): original AWCC controls and private desktop ownership |
| Worker protocol | [PresetWorker.cs](src/PresetWorker.cs): bounded JSON input, process isolation, session mutex, result handling, and timeouts |
| CPU readback | [CpuPresetReader.cs](src/CpuPresetReader.cs): read-only XTU contract and OEM profile matching |
| Window state | [WindowPreferences.cs](src/WindowPreferences.cs), [WindowPlacementSession.cs](src/WindowPlacementSession.cs): persistence, display identity, and DPI restoration |
| Appearance | [WindowAppearance.cs](src/WindowAppearance.cs): native frame and embedded icon |

The dashboard sections are partial declarations of the same `DeckWindow` class.
They share one sampling lifecycle and one request state; splitting the files does not
create additional windows, collectors, or control paths.

## Execution paths

**Monitoring:** `Collector.Sample` reads system, CPU, GPU, AWCC, and network data into a
snapshot. It forces a fresh CPU-limit read when the cooling mode changes. The window
updates on its dispatcher and retains 120 seconds of chart history in memory. A newer
worker result takes precedence over an older sampling snapshot.

**Preset changes:** the UI resolves the request through `PresetPolicy`, locks the
controls, and starts `PresetWorker`. The worker obtains fresh state and uses
`LocalPresetBackend` to invoke AWCC or the allowlisted cooling setter. Four matching
reads confirm completion. The UI then invalidates cached readback and displays actual
state. The background bridge owns and cleans up only the frontend it starts.

Keep the hardware-specific rules in the preset/firmware layer. UI code should consume
readback and the shared mode catalog; it should not infer OC state from a clicked button
or duplicate firmware IDs. Preserve bounded requests, failure states, and ownership
checks when changing the worker. The detailed contract is in [PRESET-CONTROLS.md](PRESET-CONTROLS.md).

## Conventions and local files

C# uses four-space indentation, expanded control flow, and braces on their own lines;
see [.editorconfig](.editorconfig). Keep each reader or transport focused on one source.
Add new production files directly in `src`; application and core test builds discover
them automatically.
Maintain English UI text and document externally visible changes in [CHANGELOG.md](CHANGELOG.md).

The icon source is [assets/HardwareDeck.svg](assets/HardwareDeck.svg). Regenerate it with
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Icon.ps1`.

Executables, local sensor labels, inspection output, and test artifacts are Git-ignored.
Use `.local` for development output. User window preferences and startup errors live
under `%LOCALAPPDATA%\HardwareDeck`; normal monitoring does not periodically dump data.

## Documentation languages

English files (`*.md`) are the default entry points. Each has a matching Simplified
Chinese file (`*.zh-CN.md`) and a language switch below the title. When changing a
document, update both versions: preserve section coverage, commands, identifiers,
configuration examples, dates, and validation limits. Chinese document links should
point to Chinese counterparts; source and external links keep their original targets.
Keep upstream license notices in their original wording.
