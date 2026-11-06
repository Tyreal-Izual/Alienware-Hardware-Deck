# Changelog

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
