# WOR-0097: flows wants to be it's own tab, agents too crowded

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

flows wants to be it's own tab, doesn't fit in agents and that space is too crowded

From a note on the wall (20261117-103559-c99a).

## How to check

Changed: Flows is a new rail section (Flows, below Agents) with the flow list, task box, Run flow, + New (project/shared, basic/art), Edit, and the picked flow's stages. Those controls are gone from the Agents column. Commit edfbfd0 on main (branch wor-0097, merged and removed).

Try it: build and run, open any project with Agents on, click Flows in the rail. Look for: the picked flow's stages show on the right; type a task and Run flow starts the stage agents and switches to Agents; + New starts the flow-writing agent and switches to Agents; Edit opens the folder; Agents column is less crowded. Not clicked live: Run flow, + New, Edit. Screenshot-checked only the layout.
