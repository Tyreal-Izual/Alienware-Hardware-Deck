# Linked preset controls

## Acceptance status

Live acceptance passed on 2026-11-06 for the audited Aurora R16 / i9-14900KF and
AWCC 6.14.105.0. The consolidated run verified Balanced/Off, Quiet/Off,
Performance/OC01, Performance cooling with OC Off, both directions of independent
OC switching, and rejection of OC01 under Quiet and Balanced. It restored the
original Performance/OC01 state, with PL1/PL2 both reading 253 W.

The OC02 follow-up also passed: OC01 -> OC02 -> OC01, repeated OC02, OC02 -> Off,
Off -> OC02, OC02 -> Quiet/Off, OC02 rejection under Quiet/Balanced, and the main
Performance mapping back to OC01. The run restored the original P/OC01 state and
observed no visible AWCC process on the input desktop (100 ms sampling). Native
AWCC logs corroborated Overclock 02 activation and BIOS profile index 2 without a
reboot request. Evidence: `.local/oc02/live-result.json`.

The reviewed automated run passed 395 checks: 51 sensor/configuration/voltage, 32 window
state, 27 network, 49 preset policy/verification, 26 lifecycle/readback,
181 display, and 29 packaging/worker-protocol checks.
The 1080p fullscreen layout fits without scrolling at 100% and 125% scaling.

CPU readback uses `GetAllControls().ActiveValue` after the inspected
`RefreshActiveValuesFromHardwareAccess` operation. This refresh updates the service's
read cache; it does not propose/apply tuning changes. The earlier scalar getter that
returned zeros is not used. Live data validates 53 required control values.

Local evidence: `.local/awcc-sync/linked-presets-live.json`,
`cooling-policy-live.json`, and `cold-start-result.json`.

The OC02 executable and view were deployed to the project root. The previous build
and matching view/config are backed up in `.local/before-oc02`. The dashboard was
reopened with administrator access and its saved window settings. A read-only UI
check confirmed live data, P/OC01 current highlights, and an available, visible OC02
button with no pending request. The initial standard-permission UI probe could only
see the elevated window's title bar; the matching-permission probe succeeded.
Evidence: `.local/oc02/dashboard-smoke-admin.txt`.

## Controls

| Control | Cooling target | OC target |
| --- | --- | --- |
| Main Quiet | Quiet | OEM Off |
| Main Balanced | Balanced | OEM Off |
| Main Performance | Performance | OEM OC01 |
| Cooling Q / B | Quiet / Balanced | Force OEM Off |
| Cooling P | Performance | Retain verified Off / OC01 / OC02 |
| OC Off | Retain selected standard cooling | OEM Off |
| OC01 | Requires Performance cooling already selected | OEM OC01 |
| OC02 | Requires Performance cooling already selected; manual only | OEM OC02 |

Five combinations are permitted: Quiet/Off, Balanced/Off, Performance/Off,
Performance/OC01 and Performance/OC02. Main presets never choose OC02; main
Performance always returns to OC01. Quiet/Balanced cooling rejects both OC levels in
the UI and in the worker policy. The linked main preset is highlighted only when
the observed CPU and cooling combination matches; Performance cooling with CPU Off
or OC02 is shown as Independent. Pending requests lock every control, and duplicate requests
for an already verified combination perform no write.

The component controls share the same policy, transaction, verification and result
path as the main buttons. There is no separate competing CPU tuning implementation.

## Application path

Main presets invoke AWCC's original accessible preset control. Its own service
applies the full preset, including the relevant CPU and thermal operations. A small
out-of-process worker keeps the dashboard/sensor sampling responsive and serializes
changes across HardwareDeck instances in the same Windows session.

When AWCC is closed, the worker starts the installed frontend on a private Windows
desktop, without switching the user's input desktop. A Windows job owns this temporary
frontend and cleans it up even if the worker exits unexpectedly. No Dell service is
terminated. This startup mechanism was inspected live: controls became available in
about four seconds and no AWCC window was found on the input desktop.

An existing user-owned AWCC frontend is reused and minimized; it is not killed.
It must expose its Dashboard/Performance preset controls. Other pages, unavailable
controls, dialogs or unsupported versions fail instead of sending blind mouse/key
input or dismissing prompts. When needed, keyboard focus is returned only to the
requesting HardwareDeck window, after checking its owning process.

OC02 uses AWCC's native Custom CPU editor on the private desktop. Close an existing
AWCC frontend before selecting OC02; the worker will not navigate or save over a
user-owned editor. Entering Custom can apply its saved thermal policy, so the worker
reestablishes and verifies Performance cooling before selecting and saving OC2. It
checks P again immediately before SAVE, then verifies the actual CPU and cooling.
AWCC stores this as its Custom CPU choice. Main preset clicks leave Custom and
apply the normal Off/OC01 mapping. The original display-view preference is restored.

For Performance cooling with OC Off, AWCC Balanced establishes the stock OC settings first, then
the existing allowlisted thermal setter selects Performance cooling after OC
confirmation. Selecting lower cooling while OC is active uses the corresponding
full AWCC preset, and CPU Off must be confirmed. If a change cannot be verified and
CPU state is unknown/OC remains active, the worker attempts to retain or restore
Performance cooling and reports failure. External AWCC automation can still override
manual component combinations; subsequent readback updates the displayed state.

## Cooling power policy

The BIOS cooling mode changes PL1 independently of the OC clock/current settings.
Live reference measurements on this machine established:

| Cooling | Long-term limit (PL1) | Short-term limit (PL2) |
| --- | --- | --- |
| Quiet | 170 W | 253 W |
| Balanced | 180 W | 253 W |
| Performance | 253 W | 253 W |

Performance cooling with OC Off therefore keeps the stock 60x favored P-core target
and 307 A IccMax, but uses the 253 W cooling policy. It must not be compared against
the Off file's 180 W PL1 field alone. OC identity and cooling power are verified
separately; a wrong power limit still prevents success and a linked-preset highlight.

Independent combinations are live overrides. AWCC may still display the standard
preset used to establish CPU Off, and its game/power automation can override the
manual cooling choice. HardwareDeck displays the actual combined readback.

## Verification

- CPU/OC: a batched XTU read includes favored/active P-core ratios, all E-core
  individual ratios and all 16 active E-core count limits, IccMax, voltage mode and voltage offset. The OC fingerprint must match
  the local OEM Off/OC01/OC02 definition. Missing IDs, invalid/zero-filled responses,
  custom or mismatching values in those controls never count as a verified OEM OC preset.
- Cooling: read the actual firmware thermal mode through WMI and verify XTU PL1/PL2
  against the measured policy above. Together, these checks cover 53 control values.
- Completion: require four consecutive matching reads after the request. UI selection
  alone and requested settings never determine success. A fresh worker result cannot
  be overwritten by an older cached sensor snapshot.
- Sampling: reuse the XTU connection and cache ordinary CPU-preset reads for three
  seconds. Requests and observed cooling changes force fresh reads, so a new cooling
  mode is not paired with the previous mode's cached CPU state. The readback interface exposes no tuning
  setter, SDK initialization, driver installation or voltage-write operation.

The initial compatibility scope is the audited R16 / i9-14900KF OEM layout and AWCC
6.14.105.0. Administrator access is required for CPU and firmware readback. Other
models, unsupported AWCC versions and unverifiable sources disable or reject control.

The verifier checks runtime CPU targets and cooling, not stability under load or
every firmware parameter. It does not automatically reboot or accept AWCC warning
dialogs. The read-only local profile research is in [AWCC-LOCAL-PROFILES.md](AWCC-LOCAL-PROFILES.md).

Closing the dashboard stops sampling and rejects queued UI updates. An already-started
preset worker may finish its authorized operation and cleanup; a closed result pipe is
handled quietly. A timeout remains a failure even if the worker returns later. Startup
failures terminate only the suspended frontend created by that worker, using its retained
native handle. These shutdown/failure paths are regression-tested without hardware writes.
