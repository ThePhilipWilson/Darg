# AND-0008: Android: notes, docs and reports reading views

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, ui
- **Blocked by:** AND-0003
- **Created:** 2026-11-19
- **Done:** 2026-11-23
- **Moved:** 2026-11-23 11:20:00

## Description

Read-only (v1) views for notes, docs (rendered markdown, working [[links]]) and reports, plus the Home overview. Done when: each opens and renders legibly on a phone.

## Progress (2026-11-20)

API side done and pushed (`and-0008` merged to `android`, 07f57c4): `notes`, `docs`, `doc` (markdown plus every link resolved), `reports`, `report`; Home is the existing `overview`. Left: the phone screens themselves, which need the Android project from AND-0003 (none exists yet), then a check on a device that they render legibly.

## How to check

**What changed:** the phone app's Home, Notes, Docs and Reports sections, plus a doc/report reader with working links and a ✦ Fettle button that tells Fettle which doc is open. A project now opens to Home. Commit [f24e13c](https://github.com/ThePhilipWilson/Darg/commit/f24e13c) on `android`.

**Try it:** with Darg running, from the repo: `pwsh -File tools/android.ps1 -Emulator` (or with your phone on USB, without `-Emulator`). Open Workbench, then:

1. **Home:** board counts, agents with what they're doing, unread reports and the repo's last commits. Tap an unread report to open it.
2. **Notes:** the wall a column at a time on its post-it colours.
3. **Docs:** a tree with `design` closed. Open `architecture`, tap `ANDROID.md` in it: that doc opens; Back returns.
4. **Reports:** newest first, ● unread. Open one; it stays unread on the PC.
5. **✦ Fettle on a doc:** its "Fettle sees" line ends with `doc architecture.md`.
6. Stop the emulator after: `pwsh -File tools/android.ps1 -StopEmulator`.

**Look for:** text big enough to read at arm's length, nothing cut off, tables scrolling sideways, the Fettle button not covering the last row.

**Calls I made for you** (in the plan's Questions, with Keep/Change on the Design page): a project opens to Home, not Board; pictures in docs show as named chips, not loaded; reading a report on the phone leaves it unread on the PC.

**Screenshots from the emulator:** in `D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0008/` (06-doc.png and 09-report.png too).

![Home, Home scrolled, Notes](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0008/sheet1.png)
![a doc link followed, Reports](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0008/sheet2.png)

## QA (2026-11-23, passed)

Checked on 868a751 (the `android` tip, which holds f24e13c) in a worktree of its own.

- **Suite:** unit 830/830. Integration 180/181 on the first run: `PhoneBoardTests.A_tap_on_a_tile_opens_it_and_a_long_press_puts_up_its_move_sheet` (AND-0004's test, not this card's) timed out on "the tapped card to open" while the emulator was booting and the Android head was building on the same machine. It then passed 3/3 alone, 2/2 with all 38 Mobile*/Phone* tests, and in a full integration rerun (181/181). It can't be run without this card's work, since AND-0004 sits on top of it. It looks like a timing flake under load, worth watching.
- **On the darg emulator** (1080x2400, 420 dpi), against the running desktop, every How to check step passed:
  - Home loads (it shows "Loading…" for about a second on first open). It has board counts, agents with what they're doing, cards, unread reports and the repo's commits. Tapping an unread report opens it.
  - Notes shows the wall's Loose column on its yellow paper.
  - Docs is a tree with `design` closed. It opens to show `android` indented, and closes again.
  - Architecture's ANDROID.md link opens that doc, its options table scrolls sideways, and Back returns to Architecture.
  - Fettle from Architecture says "Fettle sees: Workbench › Docs, doc architecture.md".
  - Reports is newest first, with ● unread and "to check" badges. Reports opened from Home and from Reports (WOR-0123, this card's QA report) still say `read: false` on the PC.
  - Text reads easily at phone size, nothing is cut off, and the last report line and last Reports row clear the Fettle button.
- Screenshots are in `D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0008-r2-emu/`.

**Follow-up to ticket (not this card's code):** Home's In progress tile leaves out cards in a board's *first* column. The desktop's `overview` (WOR-0141) and its Home (WOR-0054) both use `BoardViewModel.InProgress()`, which skips the first and last columns as "middle columns". The Android board keeps its backlog separately, so its first column is In Progress. Home there reads "In Progress 3" and then doesn't list those 3 cards. The phone shows what the desktop sends.
