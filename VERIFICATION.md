# Validation scope

The Windows CI builds the application, runs sensor/configuration/mode contract checks,
tests a clean source checkout without an existing executable, and renders the real WPF
view with fixture data. Display tests check 1920 x 1080 at 100%, 125%, and 150% scaling,
both full height and with room for window chrome/taskbar. They check scrolling, primary
text clipping, bounded chart height, the one-second default, interval changes, stale-data state, sensor-control
reuse, and missing-sensor layout. No collector is started during display tests.
An additional constrained-height check verifies that history panels use the measured
remaining space rather than overflowing when Windows font metrics differ.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Display.Tests.ps1`.
Fixture previews are written to `.local/display-tests/renders`; their values are not live
hardware readings. These checks do not replace live testing of fullscreen, display
switching, mixed-DPI transitions, or firmware compatibility.

Live UI smoke check for the English dashboard (2026-11-05): the refresh dropdown changed
from 1 s to 5 s and back; Next display, fullscreen, and Esc restoration worked on the
connected displays. Standard-user CPU/GPU/memory readings updated and AWCC correctly
reported its permission requirement. No thermal-mode changes were made in this check.

AWCC voltage integration (2026-11-05): inspected the installed AWCC IntelCpuInfo getter,
which calls Intel XTU GetCoreVoltageValue. A getter-only elevated probe returned varying
readings (0.737 to 1.460 V in five samples). The integrated application then reported
VoltageVolts=0.751 and VoltageSource="Intel XTU · AWCC CPU voltage source", alongside
AWCC temperatures/fan RPM, with a 140 ms sample. The thermal preset remained Balanced.
No tuning SDK initialization, driver installation, or hardware-setting writes were used.
Unit checks cover permissions, connection reuse, null-on-failure, retry backoff/recovery,
invalid values, disposal, and the getter-only service contract.

Window placement persistence (2026-11-05): 32 checks cover settings round trips, invalid
files, monitor identity versus display numbering, working-area offsets, DPI changes,
and disconnected/smaller displays. Offscreen display tests also verify that they do not
write window preferences. Live runs with an isolated settings file verified exact normal
window position/size after reopening, 1920 x 1080 fullscreen plus pin restoration,
maximized restoration, and fallback from a simulated missing monitor to the available
150%-scale display while retaining the missing-monitor preference. The first-render
restore pass prevents WPF startup layout from overwriting saved fullscreen dimensions.
The pre-push review reproduced a one-pixel overflow at fractional DPI on an odd-sized
working area. Integer-size-first clamping fixes it, with a regression check. Additional
checks cover rebasing normal geometry onto the current monitor without mutating the
stored preferred-monitor snapshot.

Native frame and icon (2026-11-05): the live window showed a dark title bar and custom
icon in normal/maximized modes, after F11/Esc transitions, and after reopening with an
isolated settings file. Dark-mode readback was enabled and a native small icon was present.
The embedded icon decoded at 16/20/24/32/40/48/64/128/256 px. Display and clean-checkout
packaging checks passed with the icon embedded in the executable. Exact DWM frame colors
and corner preferences are applied where supported; older Windows versions retain the
native dark-frame fallback.

Review fixes:
- Clamp final pixel coordinates after rounding window size, including fractional DPI.
- Keep fullscreen/maximized monitor moves synchronized with remembered normal placement.
- Clear completed move intent even when a settings save makes no file changes, so a
  later monitor disconnection retains the correct preferred display.
- Fresh checkout builds on first launch; custom build output includes its resources.
- Removed the development-only pending-binary replacement path.
- Removed the headless hardware-mode cycling command and obsolete CSV GPU parser.
- Preset metadata must contain exact integer IDs; malformed values cannot enable controls.

Previously verified on Aurora R16 / i9-14900KF / RTX 4090:
- 9 temperature channels and 4 valid fan RPM channels; another channel remains unavailable.
- Explicit CPU source selection and `GetFanSensors` association reads.
- Three standard thermal modes matched readback and the original mode was restored.
- Native UI, labeled temperature charts, normal-user failure states and elevated operation.
- A 15-second steady-state observation recorded no process read/write bytes.

Limits:
- CPU voltage uses the local Intel XTU getter used by AWCC on this R16. Other AWCC/XTU
  versions and non-XTU systems are not covered by the live verification.
- Fan IDs 50, 55, 53 and 61 reported association code 1; this does not distinguish their physical positions.
- CI has no Alienware hardware and does not establish sensor compatibility on other models.
- The application still uses an elevated window for privileged operations; a service split is future work.
