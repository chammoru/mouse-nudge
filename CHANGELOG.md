# Changelog

## 1.2.1 — 2026-11-07

- Scale the window, control geometry, and fonts together at the current monitor DPI.
- Handle per-monitor DPI changes, including fractional scales, without cumulative rounding drift.
- Keep a high-scale window within the work area and make all controls reachable by scrolling on smaller displays.
- Correct the title's text height and preserve movement preferences across the update.
- Add layout checks covering 100–500%, fractional scales, native DPI-change messages, handle recreation, and small-screen scrolling, on x64 and x86.

## 1.2.0 — 2026-11-01

- Move status and the primary start/pause action above settings.
- Show the active settings independently from unsaved edits.
- Add three movement presets and canceling pending edits.
- Add the Ctrl+Shift+J global toggle with key-repeat suppression and conflict feedback.
- Add an optional automatic-stop timer with remaining-time display; pause preserves remaining time.
- Add `--pause`, `--show`, and `--quit` commands for a running instance.
- Preserve compatibility with settings from 1.1.
- Publish source, build instructions, automated checks, and portable release assets.

## 1.1.0 — 2026-11-01

- Add editable movement distance, return interval, and repeat delay.
- Save settings beside the executable and restore them on launch.
- Validate native input layout, settings, timing, and error behavior.

## 1.0.0 — 2026-11-01

- Add the initial Windows app, custom icon, taskbar window, and notification-area controls.
