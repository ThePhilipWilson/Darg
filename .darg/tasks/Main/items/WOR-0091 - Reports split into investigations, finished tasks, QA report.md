# WOR-0091: Reports: split into investigations, finished tasks, QA reports

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

reports get divided into investigations, finished tasks, qa reports

From a note on the wall (20261117-100745-d42d).

## How to check

Changed: Reports gets a third kind. `status: investigation` in a report's properties (new; card agents are now told to use it when a card only looked into something and changed no code) joins `status: qa`; everything else is a finished task. The list has a kind filter above it (All / Finished tasks / Investigations / QA reports, each with a count) and marks investigations and QA in each row.

Commit 1da1d8c on main (branch wor-0091, merged and pushed).

Try it: `dotnet build src/Darg.Desktop`, run the exe, open Reports. Drop an `.md` with `status: investigation` in the project's reports folder. Look for: the filter's counts, each kind showing only its own reports, the pick surviving a reload.
