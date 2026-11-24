# WOR-0084: change logo and ident to not have black outline

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

From a note on the wall (20261117-001520-eb2a).

## How to check

- **What changed:** the black outline is gone from the ident and the wordmark: plain pink card over teal. Each card grew by half the old stroke, so the outer shape is the same size as before. `assets/logo.svg` and `wordmark.svg` changed, and `assets/logo.png`, `darg.png`, `darg.ico` (16 to 256 px) and `wordmark.png` were re-rendered from them with Inkscape.
- **Commit:** `21df880` on `main` (pushed; branch `wor-0084` merged and deleted).
- **Try it:** run `pwsh -File tools/publish.ps1` from a terminal, or use the header's ⟳ Update. The taskbar, Start menu and desktop icons only change after that.
- **Look at:** the wordmark in the header (screenshot: `D:\GitHub\ThePhilipWilson\Workbench\.shots\wor-0084-header.png`), the title bar icon, the taskbar icon at small size, and the Light, Dark and Girlypop themes. Without the outline, the pink and teal edges are all that separate the logo from the background. Check that it still reads at 16 px and on the light theme.
