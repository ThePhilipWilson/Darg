# WOR-0230: Virtualise the agent transcript so switching agents doesn't freeze

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 19:16:36

## Description

The first trace's hotspot 2, never carded and still there (`docs/performance.md`, "Second trace" (WOR-0223)). Switching to an agent builds its whole transcript: the Agents view's transcript is an `ItemsControl` with a `StackPanel` in a `ScrollViewer`, so nothing is virtualised.

Measured again on 25 Nov (`tools/perf/load.ps1` on a throwaway instance, 4 fake chat agents of about 500 transcript entries each, 12 switches): **each switch froze the window 1.6 to 2.7 s**, about 40% of it GC; the GC heap went from 68 to 453 MB, with 18 gen2 collections and 7.1 s of GC pause in that one minute. In the first trace it was 0.6 to 1 s per switch with shorter transcripts, so it grows with how long an agent has run. The real log can't separate it out (it sits in the 217 s "in no section"), and it is the likely reason the real instance's GC heap sits at 200 to 490 MB.

**Where:** `src/Darg.Desktop/Views/AgentsView.axaml` (the `ScrollViewer x:Name="Scroll"` with the `ItemsControl` bound to `Items`), its code-behind's scroll handling, and the transcript templates in `App.axaml` (shared with Fettle).

**Done when:**
- The transcript uses a virtualising panel (or shows the newest entries with a "show earlier" step), so a switch builds only what is on screen.
- Following the newest entry while an agent works, scrolling up through the history, and folding and unfolding long entries all still work, in the Agents view and in Fettle.
- The same load run (4 agents of about 500 entries, 12 switches) holds the window under 300 ms per switch, and the doc's Second trace part records it.
- An integration test opens an agent with a long transcript and checks that only a screenful of entries is built.

## How to check

**What changed** (commit b99de18 on `android`, https://github.com/ThePhilipWilson/Darg/commit/b99de18): the Agents view's transcript, Fettle's chat and a planner's chat on the Design page are now on a `VirtualizingStackPanel`, so only the entries on screen are built. `StickToBottom` (follow the newest entry) now stops following only when you scroll up away from the bottom; without that, the panel's changing size estimate pulled you back down while reading history. `TranscriptTests` (2 integration tests) fails without either change; the whole suite passed (962 unit, 288 integration).

**Measured** (`pwsh -File tools/perf/load.ps1 -Cards 4 -Transcripts 500 -Switch -Rounds 3 -Section Agents -Wait 0 -StallMs 100 -Keep`, 4 chats of 500 entries, 12 switches): before, the switches ran together into one 34.8 s freeze, GC heap 418 MB; after, no stall of 100 ms or more during the switches, GC heap 96 MB. Recorded in `docs/performance.md`, Second trace.

**To try** (after ⟳ Update, or `dotnet build` and the debug exe):
1. Agents: switch between two agents that have run a long time. The transcript should appear at once, at its newest entry.
2. In a long transcript, scroll up with the mouse wheel and drag the scrollbar up to the top. Look for: the view jumping, the scrollbar thumb resizing as you go (expected a little: the panel estimates heights it hasn't built), or being pulled back to the bottom. Then scroll back to the bottom while an agent works: it should follow new lines again.
3. Fettle: open a long chat, click a tool step's ▸ to open it and again to fold it.
4. Design: a planner's chat beside a plan still shows and follows.

Screenshots: D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0230/scrolled-top.png (real window, scrolled to the first entry of a 500-entry chat), D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0230/long-transcript.png (headless test, newest entry).
