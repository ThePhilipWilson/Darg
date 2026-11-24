# WOR-0020: Work on N: a toggle to give each batch agent its own git worktree

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** agents, board
- **Created:** 2026-11-16
- **Done:**

## Description

Follow-up to WOR-0017. Changed from a toggle to letting the agent decide (Philip, 2026-11-16: "it would be helpful if I didn't have to make that judgement every time").

Built in 0fc65ed: each card agent is told which other agents are at work in its folder. If there are any and the folder is a git repo, it decides for itself: it takes a worktree if it will edit code, build or run tests, and stays put if it only reads or edits notes and docs. It says which it chose and why, then rebases and fast-forwards back when done. Tested live in a throwaway repo, where both cards chose correctly.

Possible later: show on the card when its agent is in a worktree.
