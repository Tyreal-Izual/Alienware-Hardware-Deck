# Changelog

**English** | [简体中文](CHANGELOG.zh-CN.md)

## 2026-11-10 — Authorized elevated startup

- Make daily launch use a dedicated highest-privilege, current-user interactive task
  after one-time UAC configuration. Add a desktop shortcut and task check/repair/removal.
- Pin the task to a protected per-user installation under Program Files, with no
  arbitrary arguments, shell action, stored password, or automatic logon trigger.
- Keep the manifest asInvoker for direct EXE, test, and diagnostic behavior. Add a
  session-local GUI instance guard and preserve window-state/elevation handoff.
- Stage builds before elevation, retain existing window and sensor preferences, and
  require explicit UAC repair/update for the protected copy.
- Cover canceled setup, altered/disabled task definitions, unsafe permissions, and
  duplicate launches. Fix action-variable shadowing and inherited module-path issues
  found during real setup/shortcut validation.

- Tighten maintenance boundaries: recognize wrapped UAC cancellation, preflight
  background work before disabling the task, and preserve customized/unrelated
  shortcuts. All 465 automated checks pass.

## 2026-11-07 — Bilingual documentation

- Keep English as the default and add Simplified Chinese counterparts for all eight
  Markdown guides, including the README, development, validation, and AWCC references.
- Add language switches to each document and keep cross-document navigation in the
  selected language. Preserve commands, identifiers, and upstream license text.
- Document how to keep both language versions synchronized.

## 2026-11-07 — Standalone documentation and source organization

- Present HardwareDeck as a standalone project. Focus the README on setup, daily
  use, data sources, and compatibility; move the source map and development
  workflow into [DEVELOPMENT.md](DEVELOPMENT.md).
- Separate sampling, WMI access, snapshot data, preset backend, worker protocol,
  and the application entry point into focused source files. Split dashboard
  preset controls, charts, and sensor tiles into sections of one partial class.
- Centralize standard cooling IDs, names, and order. Remove duplicate UI mode
  mapping and request wrappers while preserving the existing control policy.
- Share compiler references and source discovery across build/test scripts, so
  new production files automatically participate in the core test build.
- Normalize C# layout and add .editorconfig conventions. All 404 automated checks
  pass; isolated icon regeneration produces the same icon bytes.

## 2026-11-07 — Current power and active limits

- Show CPU package power as `current / active PL1 W`, reusing the existing XTU
  readback. Update the limit immediately after preset completion and on new samples.
- Put GPU watts beside its `current / limit` values and remove the unit from the label.
- Explain sustained PL1 and short boost PL2 in the CPU tooltip. Clear unavailable or
  expired limits and preserve newer worker results against older samples.
- Add mode-change, missing-data, stale-readback, and wide-value layout coverage.
  All 404 automated checks passed, including 190 display checks; a read-only live
  UI check confirmed both updated power fields. See [VERIFICATION.md](VERIFICATION.md).

## 2026-11-06 — Independent OC02

- Add an OC02 button beside Off and OC01. It is selectable only with Performance
  cooling; the same guard is enforced again in the worker and before saving.
- Keep main preset mappings unchanged: Quiet/Balanced use Off, Performance uses OC01.
  OC02 is independent and is never enabled by a main preset or cooling-only request.
- Apply OC02 through AWCC's original Custom CPU editor on a private desktop. Preserve
  the display-view preference and require an existing user-owned AWCC frontend to be
  closed before this operation. No direct XTU writes or unsigned RPC bypasses are used.
- Extend CPU readback from 37 to 53 controls, checking both E-core target groups for
  44x versus 45x and rejecting partial OC02 application.
- Add policy, lifecycle, and 1080p layout regression coverage. All 395 automated
  checks pass. The native OC02/OC01/Off switch matrix and Q/B rejection passed on
  the audited R16, with the original state restored and no visible AWCC window
  detected on the input desktop. See [VERIFICATION.md](VERIFICATION.md).

## 2026-11-06 — Network history and verified preset controls

- Add matching download/upload history cards with current rates, recent peaks, automatic
  byte-per-second units, and 120-second hover inspection. Use native 64-bit counters for
  connected physical interfaces, with warmup, reset, adapter-change, and gap handling.
- Bold primary readings for an elevated secondary display. Keep temperature/network rows
  compact and equal in size, fitting 1080p fullscreen at 100% and 125% scaling.
- Replace the preset dropdown with Quiet/Balanced/Performance buttons that use AWCC's
  original full-preset workflow. Add compact Cooling Q/B/P and OC Off/OC01 controls,
  sharing one guarded transaction. Reject OC under Quiet/Balanced cooling and support
  Performance cooling with OC Off.
- Run temporary AWCC frontends on a private desktop, track them with a Windows job,
  and preserve user-owned frontends/services. Serialize requests and avoid duplicate writes.
- Verify 37 critical CPU values, firmware cooling mode, and the measured BIOS power
  policies: 170/180/253 W PL1 for Quiet/Balanced/Performance, with 253 W PL2.
- Fix review findings in timeout result isolation, closed result pipes, bounded request
  input, startup-failure cleanup, late UI callbacks, and CPU-cache refresh after a
  cooling-mode change. Add hardware-free regression coverage for each affected path.
- Document the local Off/OC01/OC02 definitions, capability-dependent GPU/memory no-ops,
  and current integration limits. Full preset/interlock hardware acceptance passed on
  the audited R16; see [VERIFICATION.md](VERIFICATION.md) and
  [PRESET-CONTROLS.md](PRESET-CONTROLS.md) for the exact scope.

## 2026-11-05 — Window persistence and native appearance

- Remember the monitor, normal-window position and size, fullscreen/maximized state,
  and pin setting in the current user's `window-state.json`.
- Restore placement using monitor identity, the current DPI, and the available working
  area. Keep the preferred monitor when it is temporarily disconnected and open on an
  available display. Save placement before an administrator restart.
- Restore fullscreen after WPF finishes its first render, and retain normal geometry
  when moving between monitors. Clamp integer pixel bounds after DPI rounding.
- Apply a dark native title bar and matching frame on supported Windows versions while
  preserving native window controls and system high-contrast preferences.
- Embed the HardwareDeck icon in the executable and window at nine sizes from 16 to
  256 pixels. Include the SVG source and an icon generation script using the existing
  .NET Framework toolchain.
- Extend persistence, display, and clean-checkout packaging validation; see
  [VERIFICATION.md](VERIFICATION.md) for the automated and hardware-tested scope.
