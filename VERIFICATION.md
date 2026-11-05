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

Review fixes:
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
