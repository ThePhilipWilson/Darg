# WOR-0078: notifications should only for when I need to make a decision

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-16
- **Done:**

## Description

From a note on the wall (20261116-234546-6f5f).

## How to check

- **What changed:** a toast and a Fettle Notifications entry now come only from an agent's question (AskUserQuestion), a permission ask, or the end of a turn that ran 15+ minutes (finished or failed). Short turns, errors in short turns, running out of usage, and flow finished/stopped are quiet; the agent's unseen dot and Home still show them. Every agent's briefing (Fettle's too) now says questions go through AskUserQuestion, never prose. This replaces WOR-0081's 2-minute rule with your 15.
- **Commit:** fb9717c on main (pushed). The rule is `src/Darg.Core/Decisions.cs`; the wiring is in `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`.
- **Try it:** update Darg (header ⟳, or `pwsh -File tools/publish.ps1` from a terminal). While looking at another tab: start an agent with "say hi" (no toast); one that asks you something with AskUserQuestion (toast + list entry); one that runs out of usage (no toast).
- **Not exercised live:** it builds clean and Core tests pass (527, 8 new for the rule and the briefing), but I didn't run the app or see a toast.

## Automated QA

Verified: fb9717c is on main. Read the diff: `Decisions.TellTurnEnded` notifies only for a known turn start of 15+ minutes (error or not); question and permission `Notify` calls remain; the usage-limit notify and the flow finished/stopped notify are removed; `Decisions.Briefing` is appended to project agents' system prompts (both branches) and to Fettle's briefing, and contains none of the shell-syntax characters. The 8 DecisionsTests pass; the app builds with 0 errors. Core suite: 526 of 527 pass; the one failure, TicketTitleTests (expects T-0001, gets T-0002), also fails at the parent commit fb9717c~1, so it isn't from this change.

Could not verify: the live behaviour (toast appearing or not, Fettle list entries, the unseen dot and Home still showing quiet endings), which needs the running app.
