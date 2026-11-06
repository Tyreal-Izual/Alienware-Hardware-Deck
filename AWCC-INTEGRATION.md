# AWCC integration architecture

Validated against locally installed AWCC 6.14.105.0 on an Aurora R16 / i9-14900KF.
Current controls and acceptance are in [PRESET-CONTROLS.md](PRESET-CONTROLS.md);
the OEM profile comparison is in [AWCC-LOCAL-PROFILES.md](AWCC-LOCAL-PROFILES.md).

## Original AWCC workflow

The frontend reads the app mode and active game GUID, then calls
`IPerformancePresets.SetPerformancePreset(gameId, preset)`. Leaving Custom also
calls `IAppState.SetCustomMode(false)`. The system GUID is
`00000000-0000-0000-0000-000000000001`; hardcoding it would miss game context.

| UI label | PerformancePreset | Firmware thermal mode |
| --- | --- | --- |
| Quiet | Quiet = 1 | 163 |
| Balanced | Normal = 2 | 160 |
| Performance | Fast = 3 | 161 |

The plugin saves the selection and signals `PerformanceControl`. That separate
event loop resolves CPU, GPU, memory, and thermal profiles, compares their current
and target IDs, and applies changed components in that order. Setter completion
does not establish hardware completion; failures and restart reasons are separate.

## Why the bridge invokes the original control

The frontend obtains service proxies through Dell's plugin manager. Local inspection
found caller-signature validation in both that transport and the lower OCControl
local-RPC path. OCControl uses
`WinTrust.VerifyProcess(pid, allowedPublicKeyPins)`. A getter-only probe through
the installed RPC client did not establish usable direct access. No trust store,
signature check, vendor binary, service permission, or process identity was changed.

The WinUI preset list exposes Invoke. Its item-click handler sets `_userClick`,
allowing the selection-change handler to apply the preset. Selection alone can
change the visual selection without entering this workflow. The bridge therefore
requires `InvokePattern` and has no selection-only fallback for a preset.

This delegates the app/game context and complete application sequence to AWCC,
instead of reconstructing them or writing OC values independently.

## Background execution

Each change uses a bounded worker process and a session-local mutex. Ordinary
monitoring does not keep a private AWCC frontend running.

- A verified user-owned frontend is reused and minimized. It must expose the
  Dashboard/Performance controls. Other pages are not navigated, and dialogs are not
  dismissed, to avoid discarding edits or accepting prompts.
- With no existing frontend, the worker creates a private Windows desktop and launches
  AWCC there without switching the input desktop. The new process is created suspended,
  assigned to a Windows job, then resumed.
- Only the worker-owned frontend is terminated. Job closure cleans up owned processes;
  startup failures also terminate the exact suspended-process handle. User-owned
  frontends and Dell services are preserved.
- Closed result pipes are handled quietly, queued UI work is rejected after close,
  and a late success cannot replace a timeout. Cooperative stop checks prevent new
  bridge writes once the worker observes cancellation.

An already-accepted AWCC service operation cannot be assumed canceled by closing its
frontend. Hardware changes are not atomic. Failures remain unverified and subsequent
requests must obtain fresh state.

Cold-start inspection found controls on the private desktop in about four seconds
and no AWCC window on the input desktop. Full switching was subsequently validated
through the worker; the acceptance report records the exact scope.

## Readback and independent cooling

The verifier reads 53 critical XTU active values after refreshing the hardware read
cache, plus the WMI thermal mode. OC identity uses ratios, IccMax, voltage mode and
offset. BIOS cooling power is checked separately: Quiet/Balanced/Performance use
170/180/253 W PL1, respectively, and 253 W PL2 on the audited machine.

For Performance cooling with OC Off, AWCC Balanced establishes the stock OC settings;
the coordinator verifies them before applying Performance cooling through the
allowlisted thermal setter. The resulting PL1 is 253 W, not the Off file's 180 W.
Main presets still use the original full AWCC workflow.

Four consecutive matching reads confirm a request. A cooling-mode change forces
fresh CPU values, avoiding a combination of new cooling and old cached CPU data.
AWCC automation can override independent combinations; the dashboard follows readback.

The GPU tuner returns success without tuning when its capability flag is false,
as observed for this RTX 4090. That does not prove a GPU clock increase. Memory OC
is also unavailable in the inspected runtime. OC02 is exposed as a manual CPU choice
through AWCC's native Custom CPU editor. Main presets never choose OC02.

For OC02, the worker requires Performance cooling before entering Custom, opens the
CPU Advanced View, selects OC2, and invokes SAVE. It reestablishes and verifies
Performance cooling after Custom applies its saved thermal policy, then checks P
again immediately before saving. This path requires a private frontend: an existing
user-owned AWCC window is left untouched and the request asks the user to close it.
The display-only Performance/Thermal preference is restored after the operation.
AWCC retains the selected CPU Custom preset; switching a main preset leaves Custom
and applies that main preset's original Off/OC01 mapping. The XTU reader checks both
the 16 individual E-core ratios and 16 active-count limits to distinguish OC01 (44)
from OC02 (45). Partial application never verifies as either profile.

## Compatibility and artifacts

The frontend version and OEM CPU layout are checked before control. Missing controls,
unsupported versions, unavailable reads, or mismatches fail instead of falling back
to a thermal-only main preset. Administrator access is required for CPU/firmware
readback. AWCC retains its own configured automation and network behavior.

Other hardware, languages, firmware policies, game transitions, and first-use dialogs
are outside the current acceptance scope. The implementation does not install drivers,
directly write custom CPU/GPU values, or reboot automatically.

Binary inspection output, hardware logs, local configuration and compiled probes stay
under the Git-ignored `.local` directory and are not uploaded with the repository.
