# WOR-0245: Per-project max running agents with pending state

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-27
- **Done:**
- **Moved:** 2026-11-27 11:26:50

## Description

Add a setting in each project setup for the maximum number of agents running at once. When that number is reached, any new agent is put into a pending state instead of starting, and is started automatically once the number of running agents drops below the maximum. Pending agents should be visible in the Agents list and on their card, start in the order requested, and the cap should apply to every way an agent can start (work-on, queues, flows, Improve, QA). Related: WOR-0244 queue batch size, which limits per queue rather than per project.

## How to check

**What changed:** Setup has a new box per project, *Most agents at work at once* (empty means no limit; saved as `maxAgents` in instance.json; the API's `max-agents {project, max}` sets it too). Past the limit, a new agent from any way in (Work on, Work All, queues, flows, Improve, QA, rework, chats from the Agents box, planners) is **waiting to start**. It shows in the Agents list and on its card's tile as `waiting to start · 2nd in line`, with a ▶ Start now button above its transcript, and in a *Waiting to start* sub-column under By state. It starts by itself when an agent at work stops, first asked first. Forgetting it (×) takes it out of the line. It survives a restart and waits again in its place. Changing the number in Setup no longer rebuilds the project, so agents keep running.

**Choices I made for you** (say if any is wrong):
- *At work* means starting or mid-turn, the same count as the rail's Agents pip. An agent waiting on your answer doesn't count, so a question doesn't hold up the line.
- Only **new** agents wait. A message to an agent that has already started still goes at once: yours, the carry-on after a usage reset, or the resume after a crash (WOR-0221). This can briefly put the project over the limit.
- Chats typed in the Agents box are held too. Fettle's own chats are not, because they belong to the whole instance rather than one project.
- A waiting agent's model is picked while it waits, so it starts at once when its turn comes.

**Branch and commits:** merged into `android`: aebbe23 (the feature), c73258e (a test helper: a wait no longer fails on a file in use).

**Try it:** after ⟳ Update (or `pwsh -File tools/publish.ps1` from a terminal), open ⚙ Setup → this project → set *Most agents at work at once* to 1 → Save. On the board, Work on two cards (or ▶ Work All on three).
- Look for: the first card says `agent working`, the others `agent waiting to start · 1st in line` / `2nd in line`. In Agents, the waiting ones are grey, with the line "Waiting: 1 of this project's agents is at work, and its most at once is 1 (Setup). Starts by itself when one stops." and ▶ Start now.
- When the first one's turn ends (or it asks you something), the next one starts by itself.
- Clear the box and Save: everything waiting starts at once.

Screenshots from the integration test (fake agents, cap 1): ![board](D:/GitHub/ThePhilipWilson/Darg/.shots/wor0245-keep/board-waiting.png) ![agents](D:/GitHub/ThePhilipWilson/Darg/.shots/wor0245-keep/agents-waiting.png)

**Checked:** `AgentCapTests` in both test projects: the line, its order, its place numbers, the tile, Improve held as well as Work on, taking the cap off, Start now, Setup keeping agents alive, and a restart keeping the waiting ones. Each test failed with its piece of the code broken. The whole suite (`pwsh -File tools/test.ps1`) passes: 1004 unit and 316 integration tests. Not tried with real Claude agents or by clicking in a running app.
