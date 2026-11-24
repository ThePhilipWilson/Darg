# WOR-0161: Profile the app to find what is slowing it down

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 18:39:24

## Description

The app has started running slowly. Add profiling (renderer and main process: CPU, render/re-render timings, IPC, file watching, memory) and capture a trace under typical use, so we can see what is actually slow before changing anything. Done when: there is a repeatable way to profile, and the main hotspots are identified and written up.
