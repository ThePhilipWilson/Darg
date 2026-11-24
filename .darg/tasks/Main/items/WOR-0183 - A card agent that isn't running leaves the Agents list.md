# WOR-0183: A card agent that isn't running leaves the Agents list

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-21
- **Done:**
- **Moved:** 2026-11-22 23:37:55

## Description

> bug: still lots of orphaned agents.

(Said to Fettle, 2026-11-21 00:38.)

> Once a task, be that QA, actually building, whatever, has finished, there shouldn't be an agent still in the agent list.

(Philip, answering the decision below, 2026-11-22 05:53.)

Every work, rework, QA or Improve agent on a card leaves the Agents list, and its saved file goes, once it isn't running. That covers a turn that ended well, one cut off mid-turn (crash, close, ⟳ Update), one saved with only its prompt, and the agent that asked a card's decision: the decision stays on the card. An agent at work, waiting on a question or permission, or set to carry on after a usage reset stays. Chats are left alone. Philip decided this on 2026-11-22 with the 11 leftover agents in front of him (see the last QA note).

Where things stand at 6a68635: agents whose turn ended well already go. After Philip's update on 2026-11-22 his Workbench list went from 34 agents to 14. Two kinds still stay:
- **Interrupted (8):** `Finished` needs the turn to have ended ("Turn done"), so `TaskOver`, `ForgetReplacedBy` and Clear finished all skip an agent saved mid-turn. On the real list: the AND-0003, AND-0005 to AND-0009 agents cut off by the 2026-11-20 crash, WOR-0176's QA `e007a464ce` and WOR-0186's old work agent `a9f41bc275`.
- **On a waiting card (3):** WOR-0118's three QA agents are kept by the For Human Attention rule (`AttentionCardIds`), which this card drops.

**Where:** the Agents section's list and its Clear finished button.
- `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`: `Finished` / `Settled` (only a turn that ended counts), `TaskOver` and `AttentionCardIds` (the waiting-card rule), `ForgetReplacedBy`, `IsFinished` / `ClearFinished`, `RefreshCards`.
- `src/Darg.Desktop/ViewModels/MainViewModel.cs`: where `Agents.AttentionCardIds` is set (~line 586).
- `src/Darg.Core/CardDecision.cs`: once the asker has gone, a free-text answer goes to a fresh card agent (`AnswerPreface`).
- Tests: `tests/Darg.Desktop.Tests/ReplacedAgentTests.cs`, `DecisionTests`.

**Done when:**
- A saved task agent whose transcript ends mid-turn (its last item is a prompt or tool call, with no "Turn done") is gone after start, and so is its file. The same goes for one saved with only its prompt.
- A task agent on a card in For Human Attention goes once its turn ends, the asker included. The card's decision still shows in its panel and can still be answered: a pick runs its action, and free text starts a fresh card agent.
- An agent at work, waiting on a question or permission, or set to carry on after a usage reset stays. Chats on a card stay.
- Clear finished takes all of the above at once.
- `ReplacedAgentTests` covers the interrupted and asker cases, and each fails with the change taken out. The tests that expect an asker to stay (`A_saved_agent_whose_card_waits_on_the_person_stays_after_a_restart`, `An_agent_whose_card_waits_on_the_person_stays_after_its_turn`) now expect it to go. `pwsh -File tools/test.ps1` passes.
- On Philip's real instance after ⟳ Update, Workbench's Agents list holds only running or waiting agents and chats, and none of the 11 named above.

**Questions left:**
- An agent whose turn failed stays today, so the person can see why. "Any task agent that isn't running" would take that one too. Default: it stays unless Philip says otherwise.

## Decision answered 2026-11-22 23:37

**Question:** QA round 3 passed everything automation can reach. That includes a dry run of this build on copies of your 7 leftover agents still on disk, and all 7 went. Your running copy still lists those 7 because it's the 21:41 build, which doesn't have this fix. After you press ⟳ Update, is Workbench's Agents list down to running agents and chats?
**Asked by:** agent 2f3d87ad01 (QA round 3), 2026-11-22
- **Clean after update** (recommended) → done: none of the agents on AND-0003, AND-0005 to AND-0009, or WOR-0176's `e007a464ce`, is listed any more.
- **Still leftovers** → back: name the ones still listed in your notes.
- **Not updated yet** → stay: leave it here until you've updated.
**Context:** An agent running inside Darg can't update the installed copy, because closing it kills the agent's own host. Updating cuts off every agent at work (4 right now: WOR-0176, WOR-0190, WOR-0188 and this QA agent), and by your rule those are removed on restart, so update when nothing you want to keep is mid-turn. The screenshot is the dry run: this build started on copies of the 7, and the list is empty.

![this build started on copies of your 7 leftover agents: none listed](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0183-r3-live/B1-real-copies.png)

**Answer:** Clean after update (2026-11-22 23:37)

## Decision answered 2026-11-22 21:44

**Question:** QA round 2 passed everything automation can reach (full suite, live run, the three round-1 findings fixed). The last line needs your real instance: after you update Darg (⟳) and press Clear finished in Workbench's Agents, does each card have at most one settled agent left?
**Asked by:** agent QA (round 2), 2026-11-22
- **Yes, the list is clean** (recommended) → done: you updated, the piled-up QA, rework, work and Improve agents went, and only chats, running, failed and waiting-on-you agents are left.
- **Still orphans** → back: some settled task agents survived; name the cards in your notes.
- **Not updated yet** → stay: leave it here until you've updated and looked.
**Context:** Your installed copy (D:\Apps\Darg) was built on 2026-11-20, before any WOR-0183 work, and your real Workbench list holds 34 saved agents (7 QA on WOR-0183 itself, 5 QA on WOR-0176, 3 QA on WOR-0118). An agent inside Darg can't update the installed copy, so this one is yours.

**Answer:** take a screenshot of the agents list. there are lots. lots and lots. you are clearly doing something fundamentally wrong to not be able to see them. I want a count of how many you think there are (2026-11-22 21:44)

## Decision answered 2026-11-22 05:53

**Question:** QA passed everything that can be automated; the last Done-when line (on your real instance no card has more than one settled agent listed) needs your instance. Mark the card done?
**Asked by:** agent QA, 2026-11-22
- **Done, it looks right on mine** (recommended) → done: you updated, pressed Clear finished and each card has at most one settled agent.
- **Send back** → back: the real list still has orphans; say which cards in your notes.
- **Not checked yet** → stay: leave it here until you've updated and looked.
**Context:** Automated checks all pass (see QA notes below). The real-instance line can't be automated: an agent inside Darg can't publish or update the installed copy, and I won't touch your real instance's saved agents.

**Answer:** took a screenshot, there are still lots of agents. Once a task, be that QA, actually building, whatever, has finished, there shouldn't be an agent still in the agent list. (2026-11-22 05:53)

## How to check

Rework 2 (2026-11-22, agent 802a6c156d): `android` at [b9e5041](https://github.com/ThePhilipWilson/Darg/commit/b9e5041), branch `wor-0183` (merged, deleted).
- **What changed:** a work, rework, QA or Improve agent goes, file and all, once it is idle or saved, unless it is at work, waiting on you, set to carry on after a usage reset, or failed. "Failed" means its turn ended in an error; for a saved agent, the transcript ends in a red error note. An agent cut off mid-turn (crash, close, ⟳ Update) or saved with only its prompt now goes. The For Human Attention keep (`AttentionCardIds`) is gone: the asker goes too, and the decision stays on the card. A pick still runs its action, and free text starts a fresh card agent (`AnswerPreface`). The one keep left: when a card lands in attention with no decision, its mover is held until Darg has asked it for one (`Hold`/`Release`). Clear finished and `ForgetReplacedBy` use the same test.
- **On your instance (the last Done-when line, the one I can't do):** press ⟳ Update, then open Workbench → Agents. None of these should be listed: WOR-0118's three QA agents (`65db705a28`, `6467294f9f`, `305d84e008`), the six Android ones (AND-0003 `8862b5454e`, AND-0005 `bad64f25a4`, AND-0006 `fb35eaa183`, AND-0007 `ad128359f5`, AND-0008 `26a16457f4`, AND-0009 `dd860be32e`) or WOR-0176's QA agent `e007a464ce`. `a9f41bc275` was already off disk. Only running or waiting agents, failed ones and chats should be left. WOR-0118's decision should still be in its card panel.
- **Warning:** an agent running when you update is cut off, and by your rule it is then removed on restart. Update when the list has no agent at work you want to keep (this agent's own file will go too).
- **Tests:** `pwsh -File tools/test.ps1 -Integration -Filter "ReplacedAgentTests|DecisionTests"` (28). The new `A_saved_task_agent_cut_off_mid_turn_goes_after_a_restart_with_its_file`, `Clear_finished_takes_every_task_agent_that_isnt_running`, `A_saved_agent_whose_card_waits_on_the_person_goes_after_a_restart` and `An_agent_whose_card_waits_on_the_person_goes_after_its_turn_and_the_decision_stays_on_the_card` all failed on 6a68635. `A_card_landing_without_a_decision_gets_its_agent_asked_for_one` fails with `Hold` taken out.

QA round 1 rework (2026-11-22, agent 0d3bf5809b): `android` at 6a68635.
- **Restart no longer deletes askers.** The first check at start ran before the project said which cards wait in For Human Attention, and "don't know" counted as "not waiting". Now `Agents.AttentionCardIds` is set before that first `RefreshCards` (MainViewModel), reads every board on disk (not only the boards on screen), and while it is unset no task agent goes.
- **A forgotten agent stays forgotten.** Its session's exit arrived after `Forget` and saved it back to disk; `Forget` now marks it `Gone`, and `Save` does nothing for a gone agent.
- **Improve agents** go through `TaskOver` only (their separate go-when-finished entry is removed), so they also stay while their card waits on you, and the Improve test now fails with `TaskOver` off.
- Tests: `pwsh -File tools/test.ps1 -Integration -Filter ReplacedAgentTests` (8). The new `A_saved_agent_whose_card_waits_on_the_person_stays_after_a_restart` (QA's scratch test, adopted) and `A_finished_task_agent_is_not_saved_back_once_it_has_gone` fail with the fix out. `DecisionTests` saved a forgotten agent to read its transcript; it now marks that step.
- To try live: `.shots/qa-0183-live/live-0183.ps1 -Wt <worktree at 6a68635>` as in the QA notes. `ask1` on QAC-0003 should still be listed after start, with its file still there; the qa-all agent's `<id>.json` should not come back after it leaves the list.
- Still open, your call from QA round 1: a free-text answer to a decision goes to a fresh card agent, not the asker.

Earlier rework (Philip, 2026-11-22: "once a task has finished, there shouldn't be an agent still in the agent list"): committed to `android` as fbe1729 (CLAUDE.md fdac5fa). A work, rework, QA or Improve card agent now leaves the list as soon as its turn ends well (`AgentsViewModel.TaskOver`), on every state change, board read and Clear finished. It stays if its card is in For Human Attention (its decision shows on it), if it is working or waiting on you, or if its turn failed.

1. Update Darg (⟳), open the Agents section of Workbench, press **Clear finished**: every settled work, rework, QA and Improve agent goes at once; what is left is chats, running agents, failed ones and agents on cards waiting on you.
2. Run QA on a card and let it finish: the QA agent goes when its turn ends (its verdict is in the card and the QA report). Same for Work on and Improve.
3. Send a card to For Human Attention from an agent: that agent stays until you answer.

Tests: `pwsh -File tools/test.ps1 -Filter ReplacedAgentTests` (6; the work-agent and QA-agent ones fail with `TaskOver` switched off). Full suite: unit 822/822, integration 151/151. Tests that assumed a finished task agent sits in the list now use a chat on the card, or move the card while the agent is at work.
Known: an agent that is listed when you answer its question is gone by then (the card leaves attention), so the answer goes to a fresh card agent. Not tried on your real instance.

## QA notes

- 2026-11-22 (QA agent, fdac5fa, worktree `.shots/qa-0183`): **Every restart deletes the agents on cards waiting in For Human Attention.** `MainViewModel.cs:590` calls `Agents.RefreshCards()` while the project is being built, but `Agents.CardAwaitsDecision = Board.CardInAttention` isn't set until line 702. While the delegate is still null, `CardAwaitsDecision?.Invoke(..) != true` is true, so `TaskOver` treats every restored settled task agent as finished and `Forget` deletes its saved file. Each ⟳ Update would wipe the asker of every card waiting on you, which breaks the card's own rule that an agent on a card in For Human Attention stays.
  - Reproduced live in the debug build: a throwaway instance with seeded saved agents (`.shots/qa-0183-live/live-0183.ps1 -Wt <worktree>`). `ask1`, on QAC-0003 (in For Human Attention with a valid `## Decision`, and `cards column=attention` lists it), is gone 5 s after start and its file is deleted. This happens with `DARG_SECTION` set to Agents or to Board.
  - Reproduced in the integration harness: `.shots/qa-0183-live/QaScratch0183Tests.cs` (also in the worktree's tests folder; not committed) seeds the saved asker, starts the app, and asserts it's still listed. It fails on fdac5fa and passes once `CardAwaitsDecision` is set before the first `RefreshCards` (I checked that as a scratch edit and reverted it). Fix suggestion: set the delegate up beside `Agents.CardBoards`, and treat a null delegate as "can't tell, keep" rather than "go". Adopt the test.
  - The existing `An_agent_whose_card_waits_on_the_person_stays_after_its_turn` does fail with the guard removed, but it only covers a live agent, not a restart.
- Minor, worth fixing in the same pass: after a QA agent leaves the list, its saved file is written back. In all 3 live runs, the qa-all agent's `<id>.json` was on disk after it had left the list; it was written 1 s after its turn ended, with "Turn done" last. A turn-end save lands after `Forget`'s `AgentStore.Delete`. At the next start it reloads and is dropped again, so nothing visible, but a forgotten agent comes back on disk. The work agent's file didn't come back.
- Minor: `An_Improve_agent_leaves_the_list_once_its_turn_ends_well` still passes with `TaskOver` switched off (`=> false`), so it doesn't guard this change. The work-agent and QA-agent tests do fail without it, as the card says.
- Passed: full suite on fdac5fa, unit 822/822 and integration 151/151, no failures. Live, with fake claude through the API and UI Automation: settled QA (one closed by Darg), work and Improve agents go on their own at start; a chat on the card and a failed agent stay; a `work-on` agent and a `qa-all` agent go once their turns end; Clear finished leaves the failed agent and the chat. Screenshots: `.shots/qa-0183-live/1-after-start.png`, `2-working.png`, `3-end.png`.
- For your call when this comes back, not a failure: a free-text answer to a decision now always goes to a fresh card agent (the asker has gone by the time the answer is sent), so the asker's session context is lost. `DecisionTests` was rewritten to expect that (`An_answer_of_my_own_reaches_a_card_agent_when_the_asker_has_left_the_list`).
- 2026-11-22 (QA round 2, 6a68635, worktree `.shots/qa-0183-r2`): **passed everything automation can reach.**
  - Full suite (`pwsh -File tools/test.ps1`): unit 825/825, integration 154/154, no failures.
  - Live in the debug build (`.shots/qa-0183-r2-live/live-0183.ps1`, round 1's script plus more checks; 0 of 16 failed): after start, `ask1` (on QAC-0003, waiting in For Human Attention) is still listed and its file is still there; the settled QA, work and Improve agents went and their files too; the chat and the failed agent stay. A `work-on` and a `qa-all` agent leave once their turns end, and their files stay gone 3 s later (round 1's file coming back is fixed). Clear finished leaves the asker and the failed agent. Screenshots `1-after-start.png` to `4-answered.png` in that folder.
  - Round 1's open point looks settled: a free-text `decide` answer on QAC-0003 now goes to `ask1` itself (fake claude log: `--resume` its session with "From QAC-0003's decision: … I picked: Use the blue one instead"), no fresh agent is started, and `ask1` leaves once that turn ends.
  - Tests that can fail: with the attention ids set after the first RefreshCards and "unknown" counted as "not waiting", `A_saved_agent_whose_card_waits_on_the_person_stays_after_a_restart` fails; with `Gone` taken out of `Save`, `A_finished_task_agent_is_not_saved_back_once_it_has_gone` fails; with `TaskOver` off, the Improve, work and QA tests (and the save-back one) fail. Scratch edits, reverted.
  - Can't be automated: "on Philip's real instance after an update, no card has more than one settled agent". An agent running inside Darg can't publish or ⟳ Update the installed copy (closing it kills the agent's own host, see CLAUDE.md), and QA doesn't touch the real instance's saved agents. Read-only count now: 34 saved Workbench agents on the installed build from 2026-11-20, which predates this work.
  - Minor, not blocking: `AttentionCardIds` reads every board from disk (`CardIndex.Read`) on the UI thread each time it's asked, which happens on each settled task agent's state change and on RefreshCards. No stall showed in the live run's `stalls.log`, but that was one small board.
- 2026-11-22 21:47 (QA round 2, after Philip's answer "there are lots and lots… I want a count"): **fails on the real instance.** Round 2 above didn't read the real list; this time it did. Philip updated at 21:41 (installed `Darg.Desktop.dll` 21:41:52, so it has 6a68635). The running copy's `agents` API, read-only, and a screenshot of Workbench's Agents (`.shots/qa-0183-r2-live/real-agents.png`): **15 listed (14 Workbench, 1 Alpha). 3 are working (WOR-0186, WOR-0187 and this QA agent); the other 11 Workbench agents shouldn't be there.** The update took Workbench's list from 34 saved agents to 14, so the "turn ended well" case works; two other kinds of agent don't go:
  - **Interrupted agents never go (8).** An agent whose last turn never ended (app crashed, closed or updated mid-turn) is saved with no "Turn done", so `Finished`/`Settled` is false forever: `TaskOver`, `ForgetReplacedBy` and Clear finished all skip it, and the list shows it as "saved · send to carry on". Real ones: the work agents on AND-0005, AND-0007, AND-0008 and AND-0009 and the QA agents on AND-0003 and AND-0006 (all created 2026-11-20 05:05 and cut off by that morning's crash; AND-0003 and AND-0006 are now in QA Ready, the rest In Progress with no live agent), WOR-0176's QA agent `e007a464ce` (cut off mid-tool by the 21:41 update) and WOR-0186's work agent `a9f41bc275` (also cut off at 21:41, and already **replaced** by a newer work agent `a9c1bc916f` started 21:43, which `ForgetReplacedBy` didn't take, because only settled agents go).
  - **Every agent on a waiting card stays (3).** WOR-0118 is in For Human Attention and all three of its QA agents (`65db705a28`, `6467294f9f`, `305d84e008`, each "Turn done", then "Session closed by Darg") stay, because the waiting-card rule keeps every agent on the card. Its decision says `Asked by: agent QA` with no id, so at most the newest one has a reason to stay; the older two are replaced and settled, but that only goes on Clear finished.
  - The live test missed both: its saved agents were all "Turn done" or failed, as the card described. A rework needs a seeded agent whose transcript ends mid-turn (last item a user prompt or tool call, no "Turn done") and two settled agents on one waiting card, as well as a real-instance read once it's updated.
  - For the rework to settle: is an interrupted task agent whose card has a newer agent, or has moved on, finished? Philip's rule ("once a task has finished there shouldn't be an agent") suggests it goes. The 6 Android ones are open work cut off by the crash, though, and someone has to decide whether they're resumed or dropped.
  - **Philip's call, 2026-11-22 (for the rework):** all 11 leftover agents go. Any task agent that isn't running leaves the list, whether its turn ended, was cut off mid-turn (crash, close, update) or never got past its prompt, and including the one that asked a card's decision: the decision stays on the card itself. Asked with the real list's 11 in front of him: WOR-0118's three QA agents (one the asker), the six Android ones saved with only their prompt, WOR-0176's QA agent and WOR-0186's old work agent cut off by the update.
- 2026-11-22 23:45 (QA round 3, b9e5041, worktree `.shots/qa-0183-r3`, agent 2f3d87ad01): **passed everything automation can reach. The real-instance line is waiting on an update only you can do.**
  - Full suite (`pwsh -File tools/test.ps1`): unit 827/827, integration 158/159. **One failure: `DoneCardTests.A_card_moved_to_done_gets_its_done_report_then_its_finished_agent_goes`** at line 39, `Assert.False(agent.Open)`. The agent had left the list but its session was open again. It then passed 5 reruns out of 5 on the same build, and it passes in the newest runs in the wor-0187 and wor-0190 worktrees. That full run was under heavy load (integration took 7 min against the usual ~1, with other agents' suites holding the test slots). This commit doesn't touch the test or the done-card path (`CardFinished`/`Forget`), so it's intermittent and not shown to be caused by this card, but I didn't prove it fails without this work. A likely cause worth a ticket: `AgentViewModel.Begin` doesn't check `Gone`, so anything that messages an agent after `Forget` (a queued after-turn action, a model pick coming in) starts a new session on an agent no longer listed.
  - Change taken out (scratch worktree `.shots/qa-0183-r3-neg`, the two view models put back to 9178691): `ReplacedAgentTests|DecisionTests` fails 6 of 28. These are the four new ReplacedAgentTests the card names, `An_answer_of_my_own_reaches_a_card_agent_when_the_asker_has_left_the_list` and `A_card_landing_without_a_decision_gets_its_agent_asked_for_one`. With only `Hold` made a no-op, the nudge test fails. Reverted.
  - Live in the debug build (`.shots/qa-0183-r3-live/live-r3.ps1`, Part A, throwaway instance, fake claude, API and UI Automation; all pass). After start, saved task agents go with their files whether cut off on a tool call, cut off on text, saved with only their prompt, saved with no items, closed mid-turn, the asker of a waiting card, or an older settled QA agent on that card. A chat, a chat cut off mid-turn, a failed agent (`Turn failed`), one whose error is followed by `Turn done`, and one set to carry on after a usage reset all stay. The waiting card's decision still reads through `card`. A free-text `decide` starts a fresh card agent whose message carries the answer, and that agent leaves after its turn. A pick (Ship → done) on a decision whose asker had gone moves the card to Done. A work agent whose card was moved to For Human Attention mid-turn with no decision stays listed (Hold), gets the nudge, then leaves after that turn. Clear finished keeps the working agent and all five keepers. The app killed mid-turn: on restart that agent and its file are gone and the keepers are still there. Screenshots `A1` to `A4` in that folder.
  - Dry run on your real leftovers (Part B): copies of the 7 still on disk (`26a16457f4`, `8862b5454e`, `ad128359f5`, `bad64f25a4`, `dd860be32e`, `e007a464ce`, `fb35eaa183`) and a copy of the boards, in a throwaway instance on this build. After start the list is empty, all 7 files are gone and no agent started (`B1-real-copies.png`). WOR-0118's three QA agents were no longer on your disk: WOR-0118 is in Done now, so the 21:41 build's done-card path took them.
  - Your running copy, read-only through its `agents` API: 11 Workbench agents. 4 are working (WOR-0176, WOR-0190, WOR-0188 and this QA agent) and the 7 above are saved. Expected, since the installed `Darg.Desktop.dll` is from 21:41 (6a68635), without this fix.
  - Can't be automated: the real-instance line after ⟳ Update. An agent inside Darg can't publish or update the installed copy (it kills its own host), and QA doesn't change your real agents. The dry run above is as close as automation gets.
  - Board note: board.json had this card under needs more work (c6) while its Status said QA Ready. I've put both on For Human Attention.
