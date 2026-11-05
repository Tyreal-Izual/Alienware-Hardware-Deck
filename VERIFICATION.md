# Validation scope

The Windows CI builds the application, runs sensor/configuration/mode contract checks,
and tests a clean source checkout without an existing executable.

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
- Live CPU voltage is not available from the existing sources on the test machine.
- Fan IDs 50, 55, 53 and 61 reported association code 1; this does not distinguish their physical positions.
- CI has no Alienware hardware and does not establish sensor compatibility on other models.
- The application still uses an elevated window for privileged operations; a service split is future work.
