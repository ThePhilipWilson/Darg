# WOR-0189: The Design page drops a reload asked for while one is running

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** planning
- **Created:** 2026-11-22
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 23:23:50

## Description

`DesignViewModel.Reload` returns at once while a reload is still running (`if (_pending) return;`). Its comment says such calls "fold into one more", but nothing runs them. So a Keep or Make cards write's reload, and the file watcher's reload after it, can be dropped while an earlier reload that read the old file finishes, and the page stays stale until something else reloads it. Found in WOR-0176's QA round 2. It makes `DesignPageTests.The_make_button_makes_the_ticked_cards_after_a_second_press_on_open_questions_and_refuses_a_thin_card` (and once `Keeping_an_agents_choice_puts_the_persons_name_on_it_and_sends_nothing_back`) time out in a full run on a loaded machine; both pass alone.

**Where:** `src/Darg.Desktop/ViewModels/DesignViewModel.cs`, `Reload()` (from WOR-0175, 62cdc09): keep a flag set by a call made while `_pending`, and when the running reload finishes (in its `Dispatcher.UIThread.Post`), clear `_pending` and run `Reload()` once more if the flag was set.

**Done when:**
- A reload asked for while one runs makes exactly one more reload once it finishes, which reads the file as it is then.
- A test fails without the fix: e.g. call Reload twice with the file changed between, holding the first read, and check the page shows the second state.
- `pwsh -File tools/test.ps1` passes, including the two Design page tests above in a full run.
