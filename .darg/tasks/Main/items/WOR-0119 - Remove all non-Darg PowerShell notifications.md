# WOR-0119: Remove all non-Darg PowerShell notifications

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:**

## Description

Remove all the PowerShell-based notifications that don't come from Darg (hooks, scripts and scheduled tasks that pop toasts or alerts), so Darg is the only thing that notifies.

First step: list what's currently firing and where each one is set up.

## How to check

**Changed** (all in `C:/Users/Philip/.claude`, not the Workbench repo, so there is no branch or commit here):
- Removed the Stop toast (`hooks/notify-complete.ps1`) and the AskUserQuestion toast (`hooks/notify-decision.ps1`): their entries are gone from `settings.json` and `settings.json.template`, and the two scripts are deleted.
- `hooks/notify-permission.ps1` still runs on PermissionRequest and still appends to `stats/permission-log.jsonl`, but no longer shows a toast.
- `README.md` lost the notify-complete line.
- No scheduled task pops toasts. Gamma's watcher and the NAS backup don't notify. The Claude Code built-in toggles `inputNeededNotifEnabled` and `agentPushNotifEnabled` in `settings.json` are left alone.

**Try it:**
- Command: `Select-String -LiteralPath C:\Users\Philip\.claude\settings.json -Pattern notify` should print nothing.
- Open a Claude Code session outside Darg, let a turn run over a minute, ask an AskUserQuestion, and trigger a permission prompt. No PowerShell toast should appear.

**Look for:** no "Task completed", "Decision Required" or "Permission Required" toast from PowerShell. Darg's own notifications should still appear for its agents.

**Not checked live:** the three hook removals have not been exercised in a real session.

## QA notes

- 2026-11-17: QA confirmed the file changes: the two scripts are gone, the template and README are updated, and notify-permission only logs. Run with a sample payload, it exits 0, prints `{}` and writes a log line. No scheduled task or startup item pops toasts. The Select-String check above does print one line (the notify-permission hook, which is meant to stay). None of the `.claude` changes are committed yet. Live check in a real session not done.
- 2026-11-17: at Philip's call, `inputNeededNotifEnabled` and `agentPushNotifEnabled` in `~/.claude/settings.json` are now `false`, so Claude Code's own built-in notifications are off too.
