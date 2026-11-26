# WOR-0225: Send back from card decision leaves the same decision open

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 15:05:00

## Description

Sending a card back from the card decision (For Human Attention) does not clear the decision. The card goes back, but the agent that asked still shows the same decision waiting for Philip. Expected: once Send back is chosen, the decision is answered and no longer listed in needs-you or on the agent.


## Seen on WOR-0220 (2026-11-25)

- QA agent 573b2891ef asked a decision on WOR-0220. Philip answered "Send back to fix the pick" at 15:13.
- The card file records the answer and moved WOR-0220 to In Progress at 15:13:23, but no rework agent was started: the card has no agent and none has ever been on it (agents list empty). It sits in In Progress, not running.
- The asking agent still showed the same decision waiting for Philip afterwards.
- So the send-back appears to half-apply: the card moves and the answer is recorded, but the rework agent doesn't start and the asking agent's decision isn't cleared. Cause not yet found; expected to be one fault in the decision "back" path.
- Expected: Send back answers the decision, clears it from the agent and needs-you, and starts an agent reworking the card.

## Cause

The agent that asked was still asking the same question in its own chat (an AskUserQuestion, so it sat in Needs you). Darg counts an agent waiting on the person as at work on the card (`IsLiveOnCard`), and `Decide` turns Back into "the answer goes to the agent at work, after its turn" whenever the card has one. That turn couldn't end while its question waited. So no QA notes were written, no rework agent started, and the chat question stayed open. The answer only reached the agent once the question was answered separately.

agents.log shows this on WOR-0220: the round-2 QA agent was **e7287eed4e**, not 573b2891ef (that one's turn ended at 14:45). It asked at 15:11:42. The card was answered at 15:13, and the chat question at 15:13:33. The card's queued answer then resumed it at 15:13:54. No `I picked` entry was added under WOR-0220's QA notes, which confirms `SendBackCard` never ran.

## How to check

**What changed** (commit e915e3f on `android`, https://github.com/ThePhilipWilson/Darg/commit/e915e3f):
- `BoardViewModel.Decide`: an agent on the card that is waiting on its own AskUserQuestion no longer holds the card. Back sends the card back and starts a rework agent, as on a card with no agent.
- When the action has run (done, back, or no arrow), that agent's question is declined. The decline carries the pick, the notes and what Darg did, and tells the agent to end its turn (`AgentViewModel.SettleQuestions`). It leaves Needs you. A "stay" pick leaves the question waiting.
- Tests in `tests/Darg.Desktop.Tests/DecisionTests.cs`: `Send_back_on_the_card_settles_the_askers_own_question_and_starts_a_rework_agent`, `Approving_on_the_card_settles_the_askers_own_question`, `A_stay_on_the_card_leaves_the_askers_own_question_waiting`. The first two fail with the fix unwired.

**Run:** `pwsh -File tools/test.ps1 -Filter askers_own_question`

**Try it live (after Update):** on a card in QA Ready, Run QA. Let the QA agent write a `## Decision` with a `→ back` option, move the card to For Human Attention, and also ask the question in its chat. Pick the back option on the card, not in the chat.

**Look for:**
- The agent's chat question shows as answered "… (on the card)".
- The agent ends its turn without redoing anything.
- Needs you no longer lists it.
- A new rework agent starts on the card with your notes.
- A dated `I picked: …` entry is under the card's QA notes.
