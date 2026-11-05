# Changelog

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
