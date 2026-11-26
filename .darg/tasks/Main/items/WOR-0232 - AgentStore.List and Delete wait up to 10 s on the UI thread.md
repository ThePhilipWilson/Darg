# WOR-0232: AgentStore.List and Delete wait up to 10 s on the UI thread

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-26
- **Moved:** 2026-11-26 10:20:00

## Description

The cause of the first trace's 10 s stalls at `Agent.Begin` (`docs/performance.md`, "Second trace" (WOR-0223)). `AgentStore.List` calls `Flush()`, which waits on the background writer for up to 10 s (`Task.Wait(TimeSpan.FromSeconds(10))`), and `AgentStore.Delete` waits the same way. Callers such as `MascotViewModel.ListChats` run on the UI thread. On 25 Nov 14:46 this froze the window **8.2 s** (stack: `MascotViewModel.Retitle > ListChats > AgentStore.List > AgentStore.Flush > Task.Wait`). On 17 to 20 Nov, seven stalls held 10.0 to 10.3 s "in no section", five of them right after `AgentSession.Start`, which matches the 10 s timeout.

Likely why it runs to the timeout: `WriteWaiting` loops until nothing is waiting, so while agents keep saving the writer task doesn't finish and `Flush` waits out its full 10 s. That is inferred from the code and wasn't reproduced.

**Where:** `src/Darg.Core/AgentStore.cs` (`List`, `Flush`, `Forget`, `Delete`, `WriteWaiting`), callers in `src/Darg.Desktop/ViewModels/MascotViewModel.cs` and `AgentsViewModel.cs`.

**Done when:**
- `List` returns saved records merged with those still waiting to be written, without waiting on the writer; `Delete` drops a waiting save without waiting, and a save already being written can't bring a deleted record back.
- `Flush` with a wait is used only on closing the app.
- A unit test keeps the writer busy (saves arriving continuously) and checks `List` returns in under 100 ms with the newest records.

## Fixed

Reproduced in a unit test: with saves arriving continuously, `List` (calling `Flush`) took 10017 ms, its full timeout, which confirms the inferred cause. Commit 8f91730 on `android`: `List` merges waiting saves and the one in hand with the files without waiting; `Delete` drops a waiting save and marks one in hand so the writer deletes the file once it lands; `Save` queues behind a save in hand instead of waiting; `Forget` is gone and `Flush` is called only by `MainViewModel.SaveAll` on closing. Three tests in `AgentStoreTests` (busy writer `List` under 100 ms with the newest records; `Delete` and `Save` during a write); whole suite passed (unit 933, integration 255).
