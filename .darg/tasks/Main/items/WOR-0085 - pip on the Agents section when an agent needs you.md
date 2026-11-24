# WOR-0085: pip on the Agents section when an agent needs you

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

The project tab in the top bar shows a "!" pip when one of that project's agents needs you (`Agents.NeedingYou`, `MainWindow.axaml`). The Agents section itself has no matching pip, so once you're inside the project there's no hint that an agent is waiting until you open Agents and look.

Add the same pip to the Agents entry in the project's section navigation, driven by the same `NeedingYou` flag (`AgentState.NeedsYou`), so the two stay in step and clear together.
