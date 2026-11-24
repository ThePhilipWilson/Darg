# WOR-0096: Reorder columns by dragging

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

want to be able to drag to reorganise columns

From a note on the wall (20261117-103235-3917).


## How to check

Changed: on the Notes wall, a column's header is now a drag handle. Commit 360bd69 on main (branch wor-0096, merged).

Try it: build and run (`dotnet build`, then `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe`, or ⟳ Update), open Notes, press on a column's name and drag sideways. Look for: a lifted picture of the header, a vertical line between columns showing where it lands, and the order kept after release and a reload. Ticketed and Archive can't be dragged and the dragged column can't land after them. Post-it dragging should be unchanged. Board lists were not touched (only the wall).
