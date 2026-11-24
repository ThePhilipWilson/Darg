# DAR-0001: Let Fettle read terminal output

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-24
- **Done:** 2026-11-24
- **Moved:** 2026-11-24 12:28:40

## Description

Fettle (the assistant) should be able to see the current output of a terminal in the Terminal section. Currently the Darg API has no command to read terminal output, so when Philip asks "can you see the current terminal output" it has to say no. Add an API command (e.g. terminal) returning the last n lines of a console, and mention it in the Fettle prompt.
