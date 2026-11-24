# WOR-0190: Clear-all-text button in the Fettle chat box

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-22
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 23:58:22

## Decision answered 2026-11-22 23:49

**Question:** Does the ✕ clear button in Fettle's input row look right in Dark, Light and Girlypop?
**Asked by:** agent QA, 2026-11-22
- **Looks fine** (recommended) → done: the card goes to done.
- **Needs a look fix** → back: sent back for more work with your notes on what looks wrong.
**Context:** Function is verified by test (clears, disabled while empty, nothing sent); appearance in the three themes can't be judged by automation.

**Answer:** swap the highlighted. at the moment it's looking highlighted when there is nothing to clear and not when there is. feels the wrong way round (2026-11-22 23:49)

## Description

> Add a button to the Fettle chat input that clears everything typed in the box in one click, so Philip does not have to highlight and delete the text by hand.

A small ✕ button beside the Fettle chat box that empties the typed text in one click. Nothing is sent and the chat itself is untouched.

**Where:** the Fettle panel's input row in `src/Darg.Desktop/MainWindow.axaml` (the `DockPanel` holding `MascotBox`, beside the 🎙 and ➤ buttons). The box is bound to `Mascot.Compose` in `src/Darg.Desktop/ViewModels/MascotViewModel.cs`, so add a `ClearComposeCommand` there that sets `Compose = ""`. Give the button an `AutomationProperties.Name` (e.g. "Clear Fettle box") and a tooltip, and use `{DynamicResource}` colours as the other buttons do.

**Done when:**
- With text in the Fettle box, clicking the button empties it and leaves focus in the box.
- The button is hidden or disabled while the box is empty.
- Nothing is sent to Fettle and the existing chat is unchanged.
- An integration test in `tests/Darg.Desktop.Tests` types into the box, presses the button (by its automation name) and checks `Mascot.Compose` is empty; it fails without the change.
- `pwsh -File tools/test.ps1` passes.

No other card covers this (WOR-0188 is Enter-sends, WOR-0118 is dictation).

## How to check

- Changed: a ✕ button (automation name "Clear Fettle box") left of the 🎙 in Fettle's input row; `MascotViewModel.ClearComposeCommand`; focus goes back to the box via `ClearFettleBoxClicked` in MainWindow.axaml.cs.
- Branch `android`, commit 65a5131 (branch wor-0190 merged ff, pushed).
- Try it: open Darg, open Fettle, type something, click ✕. Look for: box empties, cursor stays in it, nothing sent, ✕ greyed while the box is empty, and that it looks right in all three themes (I couldn't see it).
- Test: `pwsh -File tools/test.ps1 -Integration -Filter The_clear_button`.

## QA notes (2026-11-22)

- Full suite `tools/test.ps1`: unit 827/827, integration 161/161, no failures. Code present: ClearComposeCommand (disabled while empty), button named "Clear Fettle box", focus returns to MascotBox.
- Not automatable: how the button looks in the three themes. It is a visual judgement and the test produces no screenshot of the Fettle panel.
