# AND-0004: Android: board view with touch-friendly cards

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, ui
- **Blocked by:** AND-0003
- **Created:** 2026-11-19
- **Done:** 2026-11-23
- **Moved:** 2026-11-23 13:33:00

## Description

Show a project board by column, open a card to read its text, and move or create cards by touch (long-press drag or a move menu, plus a quick-add). Layout must work in portrait on a phone, e.g. swipeable columns. Done when: Philip can triage a backlog from the phone.

## How to check

**This round (rework of the QA notes below):** commits [89a3c7d](https://github.com/ThePhilipWilson/Darg/commit/89a3c7d), [9bf709f](https://github.com/ThePhilipWilson/Darg/commit/9bf709f) and [07e9f5d](https://github.com/ThePhilipWilson/Darg/commit/07e9f5d) on `android`.
- **The keyboard no longer covers what's typed.** The phone's shell (`ShellView.FitAbove`) lifts every screen above the soft keyboard by however much of it the keyboard covers, so the board's quick-add box and Add button, and Fettle's chat box, sit right above it, and drop back when it closes. The bottom section tabs ride up with it, between the box and the keyboard.
- **The last tile's ⇄ is clear of ✦ Fettle.** The board's lists leave room under their last tile for the button.

**Try it:** with Darg running on the PC, from the repo on `android`, run `pwsh -File tools/android.ps1 -Emulator` (or with a phone on USB, without `-Emulator`). Step on from Needs you to Projects, open a project and pick ▦ Board.

**Look for, with a finger:**
- Tap "Add a card to Backlog…": the box, what you type and Add show above the keyboard. Add puts the card in, and the screen drops back.
- With the keyboard up, back closes it and the screen drops back.
- Scroll a long list to its end: the last tile's ⇄ is above ✦ Fettle and a tap on it opens the move sheet.
- ✦ Fettle's chat box shows above the keyboard too.
- The header is below the clock and the tabs are above the gesture bar (an earlier try of this fix broke both).

What I saw on the emulator: ![keyboard up while typing in quick-add](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0004-rework/b4-full.png) ![end of the backlog clear of Fettle](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0004-rework/c2-full.png) ![Fettle's box above the keyboard](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0004-rework/d2-full.png)

**Still as before** (first round, [868a751](https://github.com/ThePhilipWilson/Darg/commit/868a751)): one list at a time, swiped or picked from the strip; a tap opens a card; ⇄, Move… or a long press for the move sheet; quick-add to the list showing; back closes the sheet, then the card, then the page.

## QA notes

**2026-11-23, QA round 2, on `075d493` (android HEAD, takes in 89a3c7d, 9bf709f, 07e9f5d)**

Passed, both earlier findings fixed:
- Whole suite, `pwsh -File tools/test.ps1` in a worktree at 075d493: unit 838/838, integration 201/201, no failures.
- The new tests fail without the fix: with the board list's 96 px margin and `FitAbove`'s lift taken out, `The_quick_add_box_stays_above_the_soft_keyboard` (box at y=729 under a keyboard from 520) and `At_the_end_of_a_long_list_the_last_tiles_move_button_is_clear_of_Fettle` (⇄ under the button) both fail; put back, they pass.
- On the darg emulator (1080×2400) with `adb shell input`, the app built and installed by `tools/android.ps1 -Emulator` against a debug desktop on a throwaway instance (`.shots/qa-and0004-r2-emu/home`, 18 backlog cards):
  - Quick-add: with the keyboard up, the box, the typed text and Add all show above it, with the section tabs between. Add made PQA-0021 on disk and the screen dropped back. ![quick-add above the keyboard](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0004-r2-emu/b2.png)
  - Back with the keyboard up closed it and the screen dropped back, still on the board.
  - At the end of the 19-card backlog, the last tile's ⇄ is clear of ✦ Fettle. A tap on it opened the move sheet, and To Do moved PQA-0021 in board.json and its Status. ![end of the backlog](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0004-r2-emu/d1.png)
  - Fettle's chat box and Send show above the keyboard. ![Fettle above the keyboard](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0004-r2-emu/e2.png)
  - The header is below the clock and the tabs are above the gesture bar, with the keyboard down and up.
  - The first round's gestures still work: a swipe changes the list, a tap opens a card and back closes it, a long press brings up the move sheet and back closes it.
- Noticed, not a fault: while the keyboard is up the list is short, so ✦ Fettle floats over a middle tile's ⇄ (PQA-0004's in the shot above). Scrolling moves the tile out from under it, and the keyboard is only up while typing.

Not checked: a real phone on USB (none attached). The emulator ran the same build and the same input-pane path.

**2026-11-23, QA agent workbench-a1, on `868a751` (android HEAD)**

Needs work:
- **The keyboard hides the quick-add box while you type.** On the emulator, tapping "Add a card to To Do…" brings up the keyboard over the whole bottom of the screen: the box, the Add button and the status line are all behind it, so the title is typed blind. Enter on the keyboard does add the card (PQA-0021 landed in To Do), but you can't see or fix what you typed. The app doesn't react to the soft keyboard anywhere (nothing in `src/Darg.Android` or `src/Darg.Mobile` handles it), so Fettle's chat box is probably hit the same way; a fix in the shell would cover both. Screenshot: ![keyboard over the quick-add box](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0004/.shots/emu/q1-full.png)
- **At the end of a long list, ✦ Fettle covers the last tile's ⇄.** With 17 cards in the backlog scrolled to the bottom, the last tile's ⇄ sits under the Fettle button, and tapping it opens Fettle's chat instead. The tile can still be moved by a long press or Move… in the open card. The board's list needs room under its last row, as AND-0008 gave the Docs, Notes and Reports lists. Screenshot: ![Fettle over the last tile's move button](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0004/.shots/emu/f1-full.png)

Passed:
- Whole suite, `pwsh -File tools/test.ps1` in a worktree at 868a751: unit 830/830, integration 181/181. `PhoneBoardTests` again on its own: 4/4. (Another QA session, workbench-8d, saw `A_tap_on_a_tile_opens_it_and_a_long_press_puts_up_its_move_sheet` time out once on "the tapped card to open" during a full run while an emulator booted, then pass 3/3 alone and 181/181 in a rerun. I didn't see it fail.)
- On the darg emulator (1080×2400) with real touch input (`adb shell input`), against a debug desktop on a throwaway instance (`.shots/qa-and0004/.shots/emu/home`: 18 backlog cards, one bug, one blocked):
  - A sideways swipe changes the list and the strip's underline follows, all the way to Completed with the strip scrolling along. A short sideways drag snaps back.
  - Scrolling the long backlog up and down doesn't flip lists, including with a sideways drift of 200 px over 900 px.
  - Dragging the strip scrolls it, and tapping a name there shows that list.
  - A tap opens the card, and back closes it.
  - A long press puts up the move sheet without opening the card, and ✦ Fettle steps aside while it's up. Picking In Progress moved PQA-0002 on disk and on the desktop board at once ("Moved PQA-0002 to In Progress.").
  - Back with the sheet up closes the sheet, then the next back leaves the project.
  - The tiles show the star, the bug chip and "⛔ waits on PQA-0001".
- First launch on a cold-booted emulator showed "Darg isn't responding" once. After Wait and a restart it started normally and didn't happen again. That's startup, not the board.

Not checked: a live agent's ◉ on a tile on the device (no agent ran). The headless test covers the binding.
