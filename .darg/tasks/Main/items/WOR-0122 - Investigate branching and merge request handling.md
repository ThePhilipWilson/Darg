# WOR-0122: Investigate branching and merge request handling

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:** 2026-11-18

## Description

Workbench itself commits straight to main, but other projects will use branches and merge requests, so Workbench needs to handle them.

Investigate how Workbench interacts with git today (agents, cards, commits, pushes) and what it would take to support a branch and merge request workflow. Write up findings and a proposal for Philip to react to, covering:

- Branch per card or per agent, and how branches are named and created
- Creating, viewing and updating merge requests (GitLab and GitHub)
- Showing branch and MR status on cards and agents
- Merging, and cleaning up branches afterwards
- Per-project rules, so projects that commit straight to main keep working as they do now

Investigation only; no implementation yet.
