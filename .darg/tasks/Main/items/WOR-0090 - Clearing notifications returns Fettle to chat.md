# WOR-0090: Clearing notifications returns Fettle to chat

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-17
- **Done:**

## Description

When the last notification is cleared, Fettle should switch back to the chat tab. Staying on an empty notifications tab is pointless.

## How to check

Clicking Clear in Fettle's Notifications tab now empties the list and switches back to the Chat tab (`ClearNotifications` in `src/Darg.Desktop/ViewModels/MainViewModel.cs` also sets `ShowNotifications = false`). Clear is the only thing that empties the list, so it's the only path that needed the change. Commit `e046d7f` on main (pushed). It builds clean, but I didn't try it live: getting a notification means an agent you aren't looking at has to finish.

To try it: update Darg (⟳ Update in the header, or `pwsh -File tools/publish.ps1` from a terminal). Let an agent finish while you're on another tab or project so a notification appears. Open Fettle, go to 🔔 Notifications and press Clear. You should land on the Chat tab with the compose box showing, not on an empty "None yet." list.
