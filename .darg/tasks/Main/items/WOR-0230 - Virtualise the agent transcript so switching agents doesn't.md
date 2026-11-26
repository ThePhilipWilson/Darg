# WOR-0230: Virtualise the agent transcript so switching agents doesn't freeze

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

The first trace's hotspot 2, never carded and still there (`docs/performance.md`, "Second trace" (WOR-0223)). Switching to an agent builds its whole transcript: the Agents view's transcript is an `ItemsControl` with a `StackPanel` in a `ScrollViewer`, so nothing is virtualised.

Measured again on 25 Nov (`tools/perf/load.ps1` on a throwaway instance, 4 fake chat agents of about 500 transcript entries each, 12 switches): **each switch froze the window 1.6 to 2.7 s**, about 40% of it GC; the GC heap went from 68 to 453 MB, with 18 gen2 collections and 7.1 s of GC pause in that one minute. In the first trace it was 0.6 to 1 s per switch with shorter transcripts, so it grows with how long an agent has run. The real log can't separate it out (it sits in the 217 s "in no section"), and it is the likely reason the real instance's GC heap sits at 200 to 490 MB.

**Where:** `src/Darg.Desktop/Views/AgentsView.axaml` (the `ScrollViewer x:Name="Scroll"` with the `ItemsControl` bound to `Items`), its code-behind's scroll handling, and the transcript templates in `App.axaml` (shared with Fettle).

**Done when:**
- The transcript uses a virtualising panel (or shows the newest entries with a "show earlier" step), so a switch builds only what is on screen.
- Following the newest entry while an agent works, scrolling up through the history, and folding and unfolding long entries all still work, in the Agents view and in Fettle.
- The same load run (4 agents of about 500 entries, 12 switches) holds the window under 300 ms per switch, and the doc's Second trace part records it.
- An integration test opens an agent with a long transcript and checks that only a screenful of entries is built.
