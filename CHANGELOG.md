# Changelog

## 1.2.0

- **Release Date**: *06 Oct 2026*
- **Highlights**:
  - Added configuration presets for saving and switching whole config snapshots
  - Added a clamp-to-region option for the mouse-cursor overlay
  - Reorganized the settings panel into Key Overlay, Mouse Overlay and Key Layout sub-pages
  - Added a fully customizable key layout with per-key labels, bindings and widths
- **Details**:
  - New configuration **presets** (mirroring HSRTimer's preset feature): save and switch whole config snapshots from the settings panel's root page, above the sub-page entry buttons. A `default` preset is created automatically on first load and cannot be deleted; loading a preset applies it to the live HUD immediately, while the UI language, panel hotkey and preset selection stay untouched.
  - New "clamp cursor inside region" option for the mouse-cursor overlay: the cursor stops at the region edge instead of wrapping or snapping back to the centre, so it never leaves the configured region.
  - The settings panel is split into three sub-pages — **Key Overlay** (key-grid HUD), **Mouse Overlay** (cursor region and trail) and **Key Layout** (customizable key layout) — so each section is easier to find and stays compact.
  - New customizable key layout: rows stack top-to-bottom and keys run left-to-right, each key with its own display character, bound key (keyboard or mouse button), and width. Rows and keys can be reordered with up/down buttons. A width-1 key can use a dual keybind shown as two half-width labels (like the original `L`/`R` hand key), a wider key stretches like the jump bar and adapts to key spacing, and a key can be turned into an empty slot that just reserves its space.
  - Key-layout key bindings now also accept the mouse side buttons (Mouse3–Mouse6), which Unity's IMGUI events otherwise never report.

## 1.1.0

- **Release Date**: *03 Oct 2026*
- **Highlights**:
  - Added smooth fade transitions for key presses
  - Added a configurable mouse-cursor overlay
- **Details**:
  - Idle/pressed key transitions now fade smoothly (text, border, fill).
  - New "Fade Speed" slider in the settings panel to control the transition speed.
  - Settings panel also integrates into TwilightTimer (the HSRTimer fork) when HSRTimer is not available.
  - New configurable mouse-cursor overlay: a circle follows mouse movement inside a configurable region, wrapping to the opposite edge or returning to the centre, with configurable colour/alpha and a directional trail that stretches with speed. The trail tracks direction changes immediately and retracts on a sharp reversal so it never sweeps through a turn. The region can also be shown as a colour-tinted backdrop behind the cursor. A "raw mouse input" option (on by default) keeps the cursor moving even when the system pointer is pinned to a screen edge.

## 1.0.0

- Release Date: 09 Sep 2026
- Highlights: First release
- Details:
  - Initial input-overlay HUD with configurable key-grid styles.
  - Standalone settings panel and optional HSRTimer panel integration.
  - Config persists through HSRTimer's `SettingsSaved` event when integrated.
  - Localization (English + Simplified Chinese) following HSRTimer's spec.
