# WOR-0113: Story-based archiving for done reports

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** reports, archive
- **Created:** 2026-11-17
- **Done:**

## Description

story/feature based archiving for done reports

I want to be able to see all the reports related to a feature in one place and to pull them all out of the main view. Otherwise there is too much to look at.

From a note on the wall (20261117-155722-80da).

## How to check

Changed: Reports has a Story filter (a report's story is the board its card is on) and an archive. Commit 829aced on main (branch wor-0113, merged and pushed).

1. Build and run Darg from D:/GitHub/ThePhilipWilson/Workbench (dotnet build, then the exe), open Reports.
2. Pick a story in the new dropdown above the list: it lists only that story's reports, each story with its count.
3. Click Archive story: its finished reports leave the list (QA reports and undecided decision reports stay). Archive (n) at the top right shows them, grouped by the same dropdown; Restore brings the picked story back.
4. Files go to reports/archive/<story>/ beside the reports.
5. A card's report link on the board should still open an archived report.

Not seen: how it looks; only the logic is unit tested (ReportArchiveTests).

