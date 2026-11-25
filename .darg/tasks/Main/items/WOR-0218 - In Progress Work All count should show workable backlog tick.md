# WOR-0218: In Progress Work All count should show workable backlog tickets

- **Status:** Backlog
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**

## Description

> The In Progress column's Work All button should show the number of tickets in the backlog that can be worked on (not blocked by open cards, no live agent). On the Main board it currently shows 0 even though there are workable backlog cards.

**What's wanted:** the In Progress column's ▶ Work All counts the workable backlog cards (no open blockers, no live agent) as well as its own, and clicking it starts them too, moving them to In Progress. It shows 0 now because it only counts In Progress cards. The backlog keeps its own ✦ Improve All (WOR-0215 removed the backlog from Work All's lists; this puts the backlog back as a source for the working column's button only).

**Where:** `src/Darg.Core/BulkStart.cs` (`WorkLists`, `For`, `Label`); `src/Darg.Desktop/ViewModels/BoardViewModel.cs` (`BulkCandidates`, `BulkCards`, `BulkCount`, `StartBulk`, `WorkOnAll`); the button text is `BulkText` on the list view model in the same file; the API's `work-all` in `MainViewModel.Api.cs`. Tests: BulkStart tests in `tests/Darg.Core.Tests`, an integration test in `tests/Darg.Desktop.Tests`.

**Done when:**
- On a board with 3 workable backlog cards and nothing in In Progress, the In Progress button reads ▶ Work All (3), not 0.
- A backlog card with an open blocker or a live agent isn't counted; blocked ones show in the existing "(n, m blocked)" form.
- Clicking it starts an agent on each counted backlog card and moves them to In Progress (asking first above 3, as now).
- The backlog column still shows ✦ Improve All and its count is unchanged; the To Do first column's Work All still works.
- The API's `work-all` matches the button.
- An integration test fails without the change; `pwsh -File tools/test.ps1` passes.

**Questions left:** none. If a card is in both counts (a To Do first column and the backlog), it's counted once.
