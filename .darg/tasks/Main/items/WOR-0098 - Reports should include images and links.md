# WOR-0098: Reports should include images and links

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

reports should include images and links where appropriate

From a note on the wall (20261117-110610-ac76).


## How to check

- Changed: Reports view resolves relative image paths from the report folder (`ReportsView.axaml`, `ReportRow.Folder`); agents are told in `ReportStore.Rules` (`ReportMedia`) to include screenshots as markdown images (full path) and links where useful. Markdown view already drew images and opened links.
- Commit: 184a5ce on main (branch wor-0098, merged and pushed).
- Try: publish/build, open Reports, open any report file after adding a line like `![x](C:/some/image.png)` and `[GitHub](https://github.com)` to it in the reports folder.
- Look for: the picture shows in the report, clicking it opens it, the link opens in the browser. Also that new agent reports include such things when they have a screenshot.
