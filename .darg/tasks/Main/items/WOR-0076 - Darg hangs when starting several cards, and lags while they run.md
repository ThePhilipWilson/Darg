# WOR-0076: Darg hangs when starting several cards, and lags while they run

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-16
- **Done:**

## Description

From a note on the wall (20261116-233559-943d).

> performance issues. noticeable hanging, particularly when starting multiple tickets

Philip confirmed it's both: the window freezes when ▶ Work on N starts several cards at once, and the app stays sluggish (scrolling, typing, switching tabs) while those agents are working. Find what holds the UI thread in each case and move it off; measure before fixing, don't guess.

**Where**
- Board → pick cards → ▶ Work on N: `BoardViewModel.WorkOnPicked` → `StartWork` (`src/Darg.Desktop/ViewModels/BoardViewModel.cs`) → `Board.WorkOn` in `MainViewModel.cs` → `AgentsViewModel.Start` and `AgentViewModel.Begin` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`).
- Suspects for the click, unverified: per card, on the UI thread, `AgentSession.Start` spawns `claude` synchronously (`src/Darg.Core/Agent.cs`), `ModelProfiles.Load` reads profiles.md, `agent.Save()` writes the record, `Move` rewrites board.json; plus a Haiku process per card for the model pick (`ModelProfiles.Pick`, `Haiku.Ask`).
- Suspects for the running lag, unverified: every stream event from every agent is posted to the UI thread (`session.Event += ... Dispatcher.UIThread.Post`) and rebuilds transcript items; `Save()` after every turn; the board's file watcher reloading the whole board each time an agent edits a card; Home's 3 s `Glance()` while shown.

**Done when**
- There's a way to see a UI-thread stall (e.g. a dispatcher heartbeat that logs gaps over 200 ms) and the causes found are named in the commit.
- ▶ Work on 5 on a throwaway instance (`DARG_HOME=.shots/home`): the window keeps repainting and the agents show up without a visible freeze.
- With 5 agents working, typing in a post-it, scrolling a transcript and switching sections have no visible lag.

**Questions left**
- How many cards before it's noticeable (3? 6?), and does it also happen with one agent that's streaming a lot?

## How to check

**What changed** (commit `b7132bf` on main, pushed): measured first with a UI-thread heartbeat that writes every freeze over 200 ms to `stalls.log` in the instance folder, saying what ran in it. Five causes found and moved off the UI thread:
1. The card prompt written to claude's stdin blocked until claude started reading (0.5 to 1.3 s for 5 cards). It's queued now.
2. Every board reload, including each time an agent edits its card, rebuilt every card: ~0.4 s each with the board showing. Unchanged cards are kept now.
3. Turn-end toasts took ~100 ms each. They show from a background thread now.
4. The agent save after each turn wrote the whole transcript (~190 ms when long). It's written in the background now.
5. Haiku's model pick started its process on the UI thread. Moved off.

Measured with 5 fake agents: ▶ Work on 5 went from 1.45 s + 0.8 s of freeze to 0.13 s + 0.1 s with the board showing. With the real claude it's 152 ms.

**Try it** (once Darg is updated: ⟳ in the header, or `pwsh -File tools/publish.ps1` from a terminal):
- Pick 5 cards, press ▶ Work on 5. The window should keep moving and the agents should appear with no freeze.
- While they work, type in a post-it, scroll an agent's transcript, switch sections. There should be no hitch, including on the board when an agent edits its card.
- **When an agent finishes, a Windows toast should still pop up.** It's now sent from a background thread, and I couldn't see the screen to check this one.
- Afterwards, open `stalls.log` beside instance.json (`%APPDATA%/Workbench/home/`). Any line there is a freeze over 200 ms, with what caused it. Paste me any that look wrong.

**Not fixed, your call later:** opening an agent with a very long transcript (~450 lines) still takes about 0.5 s. A virtualised list fixed that but caused a 100 to 200 ms hitch on every new line while that agent streams, so I left it out.

**Load test to rerun:** `pwsh -File tools/perf/load.ps1 -Cards 5 -Section Board -Touch -StallMs 100` (after `dotnet build`). It uses fake agents on a copy of this board. Toasts show when their turns end.

## Automated QA

Verified: commit `b7132bf` is on main; read the Agent.cs diff (outbox channel and writer thread, DisposeAsync drains with a 3 s timeout then kills), which matches claim 1; changed-file list covers the other four causes (AgentStore.SaveLater, Board/BoardViewModel keep unchanged cards, Notifier off the UI thread, Haiku pick). `dotnet build src/Darg.Desktop` has 0 errors. Core tests: 526 of 527 pass, including all 72 Agent, AgentStore, Stall and Board tests. The one failure is `TicketTitleTests.Retitling_a_short_note_keeps_its_words_at_the_top_of_the_description` (expects T-0001, gets T-0002), which fails on its own too; it's ticket numbering in a WOR-0082 test, outside this commit's files, so not counted against this card (worth a look separately).

Not verified (needs the live app or a human): the freeze timings, no lag while 5 agents run, the Windows toast still showing, `stalls.log` contents, and the load.ps1 rerun.
