# WOR-0141: API project overview command

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

From the WOR-0135 investigation (see its Findings section). One call for "what's going on in this project?".

**Want**
- `overview {project}`: the Home tiles as JSON (cards in the board's middle columns with their agents, agents working or needing Philip, newest artifacts, last commits), plus column counts, unread reports and the uncommitted file count per repo. Reuse what `HomeViewModel` reads.

**Done when**
- The command returns those for the test project (integration test).

Effort: about 2 h.
