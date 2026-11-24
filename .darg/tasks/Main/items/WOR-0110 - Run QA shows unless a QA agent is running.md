# WOR-0110: Run QA shows unless a QA agent is running

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

The 🧪 Run QA button (WOR-0108) never appears on real cards. `BoardViewModel.CanRunQa` is `InQa && CanWorkOn`, and `CanWorkOn` is false whenever the card has any agent. Every card in QA Ready still has its finished (saved) work agent, so the button is hidden. The context-menu entry has the same `agent is null` condition (`BoardViewModel.cs` around line 1116).

Wanted: mark each agent with its purpose (work, QA, improve, rework) so a running agent can be recognised as a QA agent. Run QA shows on a card in QA whenever no QA agent is running on it, whatever other agents the card has.

- Add a purpose to agents, saved with them. Set it where they start in `MainViewModel` (Work on, Run QA, Improve, sent back, flows). Agents saved before this have none.
- `CanRunQa` and the Run QA context-menu entry check for a running QA agent, not for any agent.
- Look at the other places that treat "has an agent" as "busy" (`AgentFor` uses in `BoardViewModel`) and say which should stay as they are.

## How to check

**What changed:** agents carry a purpose (work, qa, improve, rework, flow, chat), saved in their JSON (`purpose`); older saves have none. Run QA (card panel button and context-menu entry) shows on a card in QA unless a QA agent is running on it (Starting, Working or NeedsYou). Commit `5210aeb` on `main` (branch wor-0110, merged and pushed).

**Try it:** publish or ⟳ Update to `5210aeb`, open a board, pick a card in QA Ready that already has a finished work agent. Look for: 🧪 Run QA in the card panel and in the right-click menu. Press it: a QA agent starts, the button disappears while it works, and comes back once its turn ends. Right-click menu on a card with a running non-QA agent (work) in QA should still show Run QA.

**Not exercised live:** I built and ran unit tests only (one new for the saved purpose); I did not run the app.

**Other `AgentFor` uses, left as they are** (they ask whether any agent holds the card, which is what they mean): sort/story agents' busy lists, `StoryBoards.Place`, WorkOnCards/ImproveCards, work-on refusal, moving a card between boards, the card's Open-its-agent entry. `CanWorkOn`, `CanSendBack` and `CanQaVerdict` also stay: sending back or approving while any agent is on the card would still be wrong.
