# Changelog

## 0.0.0

- **Release Date**: *Unreleased*
- **Highlights**: Placeholder
- **Details**:
  - New "clamp cursor inside region" option for the mouse-cursor overlay: the cursor stops at the region edge instead of wrapping or snapping back to the centre, so it never leaves the configured region.
- **Contributors**: Placeholder

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
