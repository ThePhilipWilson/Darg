# WOR-0240: Make the new-agent prompt box a popup

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-26
- **Done:**
- **Moved:** 2026-11-27 10:28:27

## Description

The new agent prompt box in general should be a popup rather than sitting inline. Related to WOR-0238 (removing the default context button from it).

## Decision answered 2026-11-27 10:21

**Question:** The tests pass and the popup is wired as described; does it look and sit right when you click + New agent in Agents?
**Asked by:** agent (QA of WOR-0240), 2026-11-27
- **Looks right** (recommended) → done: the popup opens above the button, closes on start; card goes to done.
- **Needs changes** → back: sent back with your notes on placement or look.
**Context:** The popup is a Flyout, which tools/shot.ps1 can't capture, so its look and placement could not be checked by automation.

**Answer:** can't see it yet and don't have any screenshots to go off of (2026-11-27 10:21)

## How to check

- Changed: the Agents list's new-agent box is now a popup. `+ New agent` (bottom of the list) opens a flyout holding the prompt, repo picker, ◎ Context, ✎ Profiles and a Start button; starting closes it.
- Branch `main`-line commit 873f94b (branch wor-0240, merged).
- Try: build and run Darg, open Agents, click `+ New agent`, type a prompt, press Enter or Start.
- Look for: the popup opening above the button and sitting well; it closes on start; the list no longer has the inline box. Screenshot taken from the screen (not the window capture): ![the popup](D:/GitHub/ThePhilipWilson/Darg/.shots/wor0240/popup.png)

## QA notes

- Checked 2026-11-27 on branch android (contains 873f94b): tools/test.ps1 passed in full (unit 986, integration 304, 0 failed).
- EnterSendsTests now opens the flyout, asserts the inline box is gone, starts an agent with Enter and asserts the popup closes.
- Not automatable: how the popup looks and sits (Flyout is a separate popup window that PrintWindow shots miss).
