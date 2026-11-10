# Validation scope

**English** | [简体中文](VERIFICATION.zh-CN.md)

## Elevated startup follow-up (2026-11-10)

The startup contract has 56 offline checks for the fixed action, account/logon/run
level, on-demand/duplicate settings, file/task ACLs, missing/invalid task decisions,
canceled UAC, and basic-mode compatibility. Five C# instance checks cover ownership,
duplicates, release, user/session naming, and elevation handoff. These tests do not
register tasks or change hardware modes.

Live setup used the current administrator account and a protected Program Files copy.
The first attempt exposed a case-insensitive Action variable collision; it registered
no task. The corrected setup passed. Shortcut validation then exposed inherited
PowerShell module-path incompatibility; explicit runtime module imports fixed it and
an authorized repair deployed the fix. Both failures have regression checks.

The actual desktop shortcut subsequently passed cold launch and repeat launch without
another UAC prompt. The compatibility Sensors entry also kept the same single process.
Read-only scheduler checks reported Healthy with InteractiveToken/HighestAvailable,
the exact fixed EXE, empty arguments, protected files, and restricted task permissions.
Window preferences were byte-identical before and after setup/relaunch. The restored
fullscreen UI showed Sensors enabled, live AWCC RPM/temperature, CPU voltage, and PL1.
CPU preset readback remained independent/unknown rather than claiming an OEM preset;
no OC/cooling buttons or hardware-mode setters were invoked in this acceptance.

Cancellation and invalid/disabled-task cases were tested offline; the working task
was left installed. Setup, repair, checking, and desktop/script launches were exercised
on the machine. Uninstall is scoped in code to the recognized current-user task and
managed shortcut; it retains application files and window preferences.
Evidence: `.local/startup/acceptance.json` and `.local/startup/shortcut-context.json`.
See [STARTUP.md](STARTUP.md) for maintenance and direct-EXE behavior.

Final regression checks passed: 465 total (51 sensor/configuration/voltage, 32
window state, 27 network, 49 preset policy, 26 lifecycle/readback, 5 GUI-instance,
56 startup-task, 29 packaging, and 190 display checks). The installed bundle hashes
matched the reviewed staging/source files. A direct basic EXE was also successfully
handed over to the managed task without UAC or a second window; evidence is in
`.local/startup/basic-handoff.json`.

Publication review added coverage for wrapped UAC cancellation, exact shortcut
ownership, and read-only repair preflight. The existing task is disabled only
after the background-operation preflight passes. Customized or unrelated
same-name shortcuts are preserved.

The reviewed launcher was deployed through the explicit repair/UAC flow. Installed
bundle hashes matched the reviewed source/staging files. A subsequent normal close
and desktop-shortcut cold launch preserved the current window-preference file
byte-for-byte; duplicate desktop/script launches kept one process. The current
preference was maximized rather than fullscreen, so this review used a fresh baseline
instead of overwriting it with the earlier installation snapshot. Evidence:
`.local/startup-review/deployment.json`.

## Documentation language follow-up (2026-11-07)

English remains the default documentation entry point, with matching Simplified
Chinese versions for all eight Markdown guides. Each pair provides reciprocal
language links, and Chinese cross-document links stay within the Chinese versions.

Documentation validation covers local link targets, language navigation, matching
heading structure, unchanged command/configuration code blocks, matching inline
technical identifiers and numeric literals, and the original MIT notice text.
This update changes Markdown documentation only.

Publication review verified 8 document pairs, 16 language links, 130 local links,
96 table rows, and 3 matching code-block pairs. No unresolved discrepancies were found.

## Source organization follow-up (2026-11-07)

All 404 existing automated checks passed after source-file separation, shared
compiler setup, centralized mode definitions, and C# formatting: 51 sensor/
configuration/voltage, 32 window state, 27 network, 49 preset policy/verification,
26 lifecycle/readback, 190 display, and 29 packaging/worker-protocol checks.
The packaging checks build from a copied source tree, including the shared compiler
script. Isolated icon regeneration uses that script and matches the tracked icon
SHA-256 exactly.

A parser-backed comparison of the original and reorganized production sources found
504 of 520 existing member token streams unchanged. The other 16 were the two
removed UI wrappers and 14 reviewed members that use the shared mode constants,
simplified catalog construction, clearer naming, or expanded state-description
control flow. Four shared mode fields were added. Formatting separately checked
valid C# syntax and exact token/literal preservation before saving each file.
Public snapshot fields, worker protocol, and hardware-control behavior are retained.
No hardware-mode writes were performed for this structural cleanup.

Final publication review repeated the build and all 404 checks under Windows
PowerShell 5.1, matching the documented launcher commands and CI shell. Reviewed
the 14 changed members, the removed wrappers, shared compiler arguments, new
source discovery, and documentation links. No blocking defects were found.

Local comparison evidence is in `.local/cleanup/member-audit.txt`; source ownership
and build instructions are documented in [DEVELOPMENT.md](DEVELOPMENT.md).

## Power display follow-up (2026-11-07)

The updated application builds and passes 190 display/scaling/refresh checks. New
fixtures cover inline watt units, Quiet PL1 changes, immediate Performance completion
updates, older-snapshot rejection, actual limits that differ from the audited policy,
unreadable/expired limits, and independently unavailable power/limit values. The
1080p layout is checked at 100%/125%/150%, including a wide `499.9 / 253 W` value;
the 125% fullscreen render was visually inspected. These tests perform no hardware
writes and reuse the existing verified limit-readback path.

The local dashboard was normally closed, updated, and reopened with its saved
window state. A read-only live UI check observed CPU `66.4 / 253 W` and GPU
`68 / 450 W`, with CPU/cooling still verified. No hardware modes were changed.
Evidence: `.local/power-display/deployment.txt`; the previous build is retained
in `.local/before-power-display`.

Final publication review (2026-11-07): all 404 automated checks passed: 51 sensor/
configuration/voltage, 32 window state, 27 network, 49 preset policy/verification,
26 lifecycle/readback, 190 display, and 29 clean-checkout packaging/worker-protocol
checks. Reviewed the power formatting, fresh-limit handling, preset completion,
and existing readback integration; no blocking defects were found. This review
made no hardware-mode changes.

## Independent OC02 follow-up (2026-11-06)

- 395 automated checks passed: 51 sensor/configuration/voltage, 32 window state,
  27 network, 49 preset policy/verification, 26 lifecycle/readback, 181 display,
  and 29 packaging/worker-protocol checks.
- New cases cover Q/B rejection of OC02, unchanged main preset mappings, preserving
  OC02 with cooling P without reapplying it, OC02/OC01 highlights, pending states,
  and mixed E-core target rejection. All 53 critical CPU controls are required.
- The real worker passed OC01 -> OC02 -> OC01, repeated OC02, OC02 -> Off,
  Off -> OC02, and OC02 -> Quiet/Off. Quiet/Balanced rejected OC02 without changing
  their verified Off state; main Performance still selected OC01.
- The run restored its original Performance/OC01 state with 253/253 W PL1/PL2.
  A 100 ms input-desktop window monitor saw no visible AWCC process. Temporary
  frontends were cleaned up. Evidence: `.local/oc02/live-result.json`.
- The local AWCC log recorded `ActivateCpuProfile(Overclock 02)`, E-core
  count limits of 45, and `BiosOcKnobs.ApplyProfile(2) -> rebootRequired=False`.
  This was the original AWCC workflow, not a direct BIOS-only or XTU write.
- 1080p fixture rendering still fits fullscreen at 100% and 125%, including the new
  button; 150% keeps the lower content accessible by scrolling. The 125% preview
  was visually inspected. This validates application/readback, not load stability.

The tested build was installed locally and reopened with sensors enabled. The
read-only deployed UI check confirmed P/OC01 as Current, OC02 as Available,
and "OC and cooling verified". Previous artifacts are in `.local/before-oc02`.

Final publication review (2026-11-06): rebuilt from source and reran all 395 checks,
including clean-checkout packaging and the 1080p fixture layouts. Reviewed the
OC02 transaction, private-frontend lifecycle, readback classification, and the saved
live acceptance evidence. No blocking defects were found. This review did not
repeat hardware writes or change the running dashboard.

## Earlier validation

The Windows CI builds the application, runs sensor/configuration/mode contract checks,
tests a clean source checkout without an existing executable, and renders the real WPF
view with fixture data. Display tests check 1920 x 1080 at 100%, 125%, and 150% scaling,
both full height and with room for window chrome/taskbar. They check a complete four-chart
fullscreen view at 100%/125%, vertical access to the network row when space is limited, primary
text clipping, bounded chart height, the one-second default, interval changes, stale-data state, sensor-control
reuse, and missing-sensor layout. No collector is started during display tests.
An additional constrained-height check verifies that history panels use the measured
remaining space rather than overflowing when Windows font metrics differ.

Network throughput (2026-11-05): 27 checks cover native structure alignment, physical
interface selection, duplicate-interface filtering, first-sample warmup, idle zeros,
64-bit counter deltas, actual elapsed time, adapter changes, counter resets, long gaps,
query failure/recovery, and rate formatting. A passive five-sample local probe identified
one active Ethernet interface and returned independently varying upload/download rates
without an administrator prompt or generated test traffic. Fixture rendering verifies
matching temperature/network card sizes, separate units, peak labels, and disconnected
states. At higher scaling the lower row remains accessible through a dark vertical scrollbar.
An integrated `--probe` run also reported connected Ethernet with `Administrator=false`
and nonzero independent receive/transmit rates.

Elevated-display readability (2026-11-06): primary metric, detail, sensor-tile, and current
chart values use bold weights, with brighter neutral readings. Display checks
cover 100%/125%/150% scaling, including text clipping and matched history-card dimensions.
The 1080p fixture preview was visually inspected; labels and axis ticks retain lighter weights.

Preset controls (2026-11-06): fixture-driven UI checks verify linked and independent
states, the OC/cooling interlock, pending highlights, duplicate-click suppression,
failed readback, power-limit mismatches, external updates, and stale/unavailable data.
Lifecycle checks cover late callbacks during close. These checks do not start a
collector or change hardware modes. The worker runs separately from sensor sampling.

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

Reviewed linked-control acceptance (2026-11-06):
- 368 automated checks passed: 51 sensor/configuration/voltage, 32 window state,
  27 network, 35 preset policy/verification, 24 lifecycle/readback, 170 display,
  and 29 packaging/worker-protocol.
  The real view fits 1080p fullscreen at 100% and 125% scaling without scrolling.
- Live hardware-refresh plus batched XTU reads returned 37 critical active settings.
  The old scalar getter that returned zeros is excluded from the implementation.
- The complete live run passed Balanced/Off, Performance/OC01, Quiet/Off, independent
  Performance cooling with OC Off, and both directions of the OC switch. OC01 requests
  under Balanced and Quiet were rejected without changing their verified states.
- Live reference measurements established PL1 = 170/180/253 W for Quiet/Balanced/
  Performance cooling, with PL2 = 253 W throughout. OC identity and cooling power
  are independently verified; Performance cooling with OC Off is not forced to 180 W.
- The final run restored and read back Performance/OC01 with PL1/PL2 = 253/253 W.
  Evidence is in `.local/awcc-sync/linked-presets-live.json` and
  `cooling-policy-live.json`.
- Deployment smoke check: the root executable matched the tested build hash and
  rendered the new controls with live basic metrics. After a canceled administrator
  launch, standard-permission mode correctly disabled privileged preset actions and
  displayed Enable sensors. The previous complete build is retained for rollback.
- A live cold-start check found AWCC controls on the private desktop in about four
  seconds and no AWCC window on the input desktop. Temporary frontend/job cleanup
  passed; no AWCC or test worker processes remained after the acceptance run.
- Review regressions exercise timeout versus late completion, closed/disposed result
  streams, bounded CRLF input, real worker rejection before backend creation, queued
  UI callbacks during close, readback retry/disposal, and fresh CPU reads on cooling
  changes. These tests do not change hardware modes. The existing live matrix remains
  the hardware evidence; failure-path fixes were validated separately.
- See [PRESET-CONTROLS.md](PRESET-CONTROLS.md) for the compatibility and verification
  scope. These earlier checks did not establish stability under load or OC02 support;
  the OC02 follow-up above adds functional application/readback coverage.

Limits:
- Local-only profile audit (2026-11-06): compared all 46 stored XTU entries in each
  of Off/OC01/OC02 and 142 unique control targets in each recorded Off/OC01 proposal.
  Off/OC01 differed in six submitted control values. OC02 adds 32 E-core target
  differences versus OC01 and two stored memory fields. At the time of that
  read-only audit it had not been activated; none of the 209 then-existing
  overclocking log files contained an exact OC02 activation entry. The later
  activation and its results are recorded in the OC02 follow-up above. State-dependent call order, unchanged-profile skipping, and memory/GPU
  capability guards were established from local code. See
  [AWCC-LOCAL-PROFILES.md](AWCC-LOCAL-PROFILES.md) for the evidence and limits.
- Initial AWCC full-preset research (2026-11-06) established the installed UI/service call path
  through read-only metadata inspection, including CPU/GPU/memory OC handling and
  client-signature validation. Direct third-party service access remains unverified.
  At that stage the tests covered thermal writes only; the integrated acceptance above
  supersedes that limitation. The local call-path evidence is in
  [AWCC-INTEGRATION.md](AWCC-INTEGRATION.md).
- Authorized background validation (2026-11-06, 13:26–13:35): a separate UI Automation
  prototype invoked Quiet/Balanced/Performance in the already-minimized AWCC frontend.
  Eight service preset requests matched 24 successful CPU/GPU/thermal service results.
  Subsequent code/capability inspection found that the eight GPU results are no-ops:
  AWCC reports this GPU as not overclockable and returns success before tuning it.
  The original Performance preset was restored. AWCC stayed minimized at the window
  observations; the user confirmed opening it manually afterward. A hidden-window
  focus transfer was found, and caller-focus return was observed in a separate local
  harness. That prototype run alone did not validate integrated buttons, invisible cold startup, memory
  OC changes, game/Custom transitions, first-use dialogs, or a production completion
  protocol. See [AWCC-INTEGRATION.md](AWCC-INTEGRATION.md) for the exact evidence scope.
- Direct OC follow-up: inspected and compared the local R16/i9-14900KF OEM CPU
  configurations. Four hardcoded getter-only requests through the installed local-RPC
  client returned false/empty responses; the server additionally validates caller
  process signing. WMI metadata confirms `BiosOCKnobs` exists, but its setter was not
  invoked. GitHub implementations were reviewed as source only. None of these checks
  validates a standalone CPU tuning backend or a BIOS-only equivalent to AWCC.
- CPU voltage uses the local Intel XTU getter used by AWCC on this R16. Other AWCC/XTU
  versions and non-XTU systems are not covered by the live verification.
- Fan IDs 50, 55, 53 and 61 reported association code 1; this does not distinguish their physical positions.
- CI has no Alienware hardware and does not establish sensor compatibility on other models.
- The application still uses an elevated window for privileged operations; a service split is future work.
