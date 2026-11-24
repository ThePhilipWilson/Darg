# WOR-0083: fettle pip is cut off so not full circle

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

From a note on the wall (20261117-001503-0ef2).

## How to check

- Changed: `src/Darg.Desktop/App.axaml`, style `Button.critter`. The corner button clipped to a rounded rectangle, cutting off the "!" and notification-count badges that overhang Fettle's circle. It now has padding (6,6,6,0) to hold them and no rounded clip.
- Branch `wor-0083`, commit `d25be08` (fast-forwarded into main and pushed). It builds clean; I did not see it running.
- Open the app (`dotnet build`, then the exe), send Fettle a job so the red "!" badge shows, or have a notification waiting so the count pip shows at top-left. Look for whole circles, not clipped ones.
