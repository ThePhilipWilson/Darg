# AND-0005: Android: agents list and transcript viewer

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, ui
- **Blocked by:** AND-0003
- **Created:** 2026-11-19
- **Done:** 2026-11-23
- **Moved:** 2026-11-23 13:55:08

## Description

List a project's agents with state and what each is doing, open one to read its transcript, and send it a message. Needs readable long output on a small screen. Done when: an agent can be watched and nudged from the phone.

## Decision answered 2026-11-23 12:57

**Question:** Is AND-0005 done, given that a long transcript opens to a blank page for several seconds before anything shows?
**Asked by:** agent (QA of AND-0005), 2026-11-23
- **Send back for a loading line** (recommended) → back: the page says "Reading the transcript…" (or similar) until the first read lands, so a slow open doesn't look like an empty agent; everything else stays as it is.
- **Done as it is** → done: the slow open only showed on the software-rendered emulator with a debug build; accept it and ticket anything a real phone shows later.
- **Keep it for your phone** → stay: leave it in For Human Attention until you've opened a long agent on the real phone.

**Context:** Everything else passed, on the emulator too. Opening a done agent with 903 entries showed a blank page for 6 to 10 s (the desktop answers all of it in 0.3 s, so the time goes on the phone: 900 rows in a non-virtualised list, debug build, software GPU). With no loading text, I took it for an empty transcript the first time.

![blank at 6 s, filled at 10 s](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0005/.shots/emu/k-pair.png)

**Answer:** Send back for a loading line (2026-11-23 12:57)

## How to check

**This round** (sent back for a loading line; on `android`, pushed: 0afcf62): opening an agent's transcript on the phone now says "Reading the transcript…" in the middle of the page until the first read lands. It goes once the entries are drawn, or when the read fails, which then says why (any failure now, not only the API's own errors). Everything from the first round is as it was; its detail follows.

**Try it:** with the desktop Darg running and an agent with a long transcript (hundreds of entries) in a project, run `pwsh -File tools/android.ps1 -Emulator` (or with the phone on USB), open the project, pick Agents and open that agent.

**Look for:** "Reading the transcript…" from the moment the page opens until the entries appear, never a blank page in between; and that it doesn't come back on later polls. A short transcript may show it only for a blink.

![reading line, headless](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0005/phone-5-reading.png)

Seen on the emulator since (the agent that asked, with the identical change built from its QA worktree, 2026-11-23 13:16): opening a 5,000-entry transcript showed "Reading the transcript…" in the middle of the page from the first screenshot through 17 s, then the transcript at its newest entry and no line by 27 s. Strip of shots: ![reading line on the emulator](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0005/.shots/emu/s-strip.png) The keyboard covering the nudge box (first round's QA) is AND-0004's shell fix, now on `android` under this. The headless test (`MobileAgentsTests.The_transcript_says_it_is_reading_until_the_first_read_lands`) holds the agent command's answer to show the line, then lets it through and checks the line goes.

**First round** (bfa0eb4 be5b152 aa6f67e 8984414 5f70e09 481b941):
- The desktop API's `agent` command takes `since` and answers entries from that index (i, total, next; 200 a page), with full text, a step's tool, result and diff, and an ask's pending state. `say` takes `queue: true` to send mid-turn. Core's `AgentFeed` / `RemoteAgent` in `src/Darg.Core/RemoteAgents.cs`.
- The phone's Agents tab (`AgentsListView`): agents needing you come first, each with its state, what it's doing and its card. It polls while on screen and leaves room under the last row for ✦ Fettle.
- Tapping an agent opens its transcript (`AgentPageView`). It follows the newest entry while you're at the bottom, folds long text to a screenful (Show n more lines), shows each step on one line with its result or diff a tap away, and has a box at the foot that nudges the agent, mid-turn too.

![agents](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0005/phone-1-agents.png)
![folded](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0005/phone-2-folded.png)

## QA 2026-11-23

Built from `android` at b9cbccd (all six of the card's commits) in a worktree of its own (`.shots/qa-and0005`).

**Passed**
- Whole suite, `pwsh -File tools/test.ps1`: 834 unit and 197 integration tests, none failing.
- Extra headless test (`tests/Darg.Desktop.Tests/QaAnd0005Tests.cs` in the QA worktree, not committed), with ten fake agents at work and a 412-wide phone. The agents list kept its scroll place over polls while every row's "doing" changed (169 → 169). The transcript opened at its end. Scrolled up to 100, it stayed at 100 while entries arrived. At the bottom, it followed new entries (gap 0). An opened long prompt (18 lines hidden) stayed open, as the same row, over polls. A Bash step with a result opens to it on a tap (label Hide), and an Edit step shows `− old / + new`.
- On the emulator (`tools/android.ps1 -Emulator` against a throwaway Darg with fake agents): the Agents tab lists the working agent first and the done ones below, each with its own name and what it's doing. Opening one lands at the newest entry. A slow drag up held still for 10 s while the step count in the header rose. Back at the bottom, it followed. The 30-line prompt showed folded with "Show 18 more lines", which opened it whole and kept it open. A nudge typed and sent mid-turn landed in the desktop's transcript of that agent (entry 214, agent still working), and the box cleared. Android back went from the transcript to the list. Screenshots are in `D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0005/.shots/emu/` (f4-agents, f5-open, f7-pair, f9-pair, g3-sent, h1-top, h2-more).

**Seen, not counted against this card**
- The keyboard covers the nudge box while typing (g-pair.png), so the nudge is typed blind until the keyboard closes. This is the same app-wide gap as in AND-0004's QA notes (nothing in the app handles the soft keyboard). The fix belongs in the shell, which AND-0004's rework covers.
- Tapping a Read step does nothing: the desktop keeps no result for Read (only Bash, PowerShell, Grep, Glob, errors and diffs), so there's nothing to show. This is consistent with the desktop. Fake claude only makes Read steps, which is why the step tap was checked headless with made-up entries.
- The first emulator run was cut off when AND-0004's rework installed its own build over the same emulator and pointed it at its own instance. Two agents can't share the one emulator. The second run was clean.

**Can't be automated here:** a real phone (none attached). The slow open of a long transcript is measured only on the software-rendered emulator with a debug build, so how it feels on the real phone needs the phone.

## QA 2026-11-23 (loading-line round)

Built from `android` at 55512e7 in a worktree of its own. Whole suite (`tools/test.ps1`): 838 unit and 202 integration, none failing, including `MobileAgentsTests.The_transcript_says_it_is_reading_until_the_first_read_lands`. The emulator evidence in How to check (5,000 entries, line shown from the first shot to 17 s, gone by 27 s) is from the same change; I did not rerun the emulator, since another agent may hold it. Not checked: a real phone (none attached), so how the slow open feels there is for Philip to notice in use.
