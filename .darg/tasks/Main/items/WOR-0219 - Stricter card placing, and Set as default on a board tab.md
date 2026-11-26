# WOR-0219: Stricter card placing, and Set as default on a board tab

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 14:05:12

## Description

> last created ticket didn't get put in the correct board. There should probably be a default board that if the agent can't decide on anything (or it's from a note) it goes to as standard, main in this case

(Philip to Fettle, 2026-11-25, while on the Merge-safe boards board. Asked to clarify: "stricter, rightclick on a board title to bring up a context menu that includes set as default"; a note's ticket is "placed like others".)

Every new card (from Fettle's new-card, a note's ticket, or + Card on the inbox) already lands on Main, which is picked by its name, and it stays there when Haiku answers "none". The miss came from Haiku confidently picking the wrong story board. The likely case is WOR-0218, a Work All count bug that has nothing to do with any story board. Two changes:

1. **Stricter placing.** Haiku moves a card off the default board only when the card is clearly part of that board's story. Anything less stays put. Note tickets are placed the same way as other cards.
2. **Set as default.** Right-clicking a board tab opens a menu with **Set as default**. The default board is where new cards land and where an unsure card stays. It replaces the name guess (Inbox, Main or Backlog, else the first board), which remains only for a project with no default set. The menu marks the current default.

**Where:**
- `src/Darg.Core/StoryBoards.cs`: `Prompt` (what Haiku is asked; make it say a near fit or a shared word is "none"), `Pick`, `Suggest`, `Place`, and `Inbox`, which should take the default board first. Keep the default in a small file beside `ShownBoards` / `BoardOrder` in the same file (for example `default-board` in the project's `.darg/`, through `ShownBoards.FileFor`'s folder).
- `src/Darg.Desktop/ViewModels/BoardViewModel.cs`: `InboxFolder` and `PlaceLater`, which today call `StoryBoards.Inbox(BoardOrder.ByName(Root.Boards))`, and `BoardTab` (around line 1714).
- `src/Darg.Desktop/Views/BoardView.axaml`: the tab template in `BoardTabs` (around line 225) gets the context menu.
- The API's `new-card` in `src/Darg.Desktop/ViewModels/MainViewModel.Api.cs` (`NewCard`) uses `InboxFolder`, so it follows the default without changes.
- Tests: `tests/Darg.Core.Tests/StoryBoardsTests.cs`, `tests/Darg.Desktop.Tests/BoardTabsTests.cs`.

**Done when:**
- Right-clicking a board tab shows Set as default. Picking it on another board makes the next Fettle card (`new-card`) and the next note ticket land there, and the choice survives a restart. A desktop integration test covers this.
- With no default set, cards still land on Main, as now.
- A live Haiku test (`DARG_LIVE=1`, as in `ChatTitleTests`) using WOR-0218's title and description and this project's board names gets "none", so the card stays on the default board. A card plainly about a story, such as "three-way merge of board.json conflicts", still goes to Merge-safe boards.
- `StoryBoardsTests` cover `Inbox` preferring a set default, and falling back to the name guess when the default board has been deleted or renamed.
- `pwsh -File tools/test.ps1` passes.

**Questions left:** none.

## Decision answered 2026-11-25 15:44

**Question:** The strict prompt sometimes leaves a plainly-story card on the default board (the Android swipe card got "none" in 1 of 10 live tries), which makes the live test fail about one run in four. How should that be handled?
**Asked by:** agent (QA of WOR-0219), 2026-11-25

![Set as default on Art after a restart, ticked](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0219/qa-art-menu-after-restart.png)

- **Keep the strict prompt, fix the test** (recommended) → back: the prompt stays as it is, and the live test asks the story cards to move on 2 of 3 tries while the "none" cards must stay on 3 of 3, so it checks the strictness you asked for without failing on a single miss.
- **Accept as is** → done: the prompt and test stay as they are. The live test is opt-in (DARG_LIVE=1) and not part of tools/test.ps1, and a miss only leaves a card on the default board.
- **Loosen the prompt** → back: plainly-story cards should always move, even if a borderline card now and then moves too.

**Context:** Everything else passed: the full suite (930 unit, 254 integration), the real window's menu (screenshot above; ✓ on Main with nothing set, on Art once set and after a restart), and new-card landing on the default across a restart. All three wrong-story cards stayed "none" on 3 of 3 tries, and the merge card went to Merge-safe boards on 3 of 3.

**Answer:** Keep the strict prompt, fix the test (2026-11-25 15:44)

## How to check

**This round** (after QA notes 2026-11-26, where the merge card stayed on the default board on about 7 tries in 10): commits 937d666, cd54b2f and d34b76c on `android`, pushed.

- **Cause:** Haiku's reply swung with what claude loaded from the folder it started in (the person's CLAUDE.md, memory, hooks). The earlier round measured the merge card reaching its board on 1 of 10 tries from one folder and 10 of 10 from another. WOR-0224 has since made Haiku read none of the person's settings (`--setting-sources ""`), which removes most of that swing by itself.
- **Fix:** Haiku now says in two or three sentences how it decided, then gives the board (or none) alone on the last line. `StoryBoards.Pick` reads only that last line, so a board named in the reasoning is never taken. `Haiku.Ask` starts claude in an empty `darg-haiku` temp folder, so no repo's git status or CLAUDE.md reaches any Haiku question (titles, queue checks and model picks too). The live test now wants a story card on its story on **4 of 6** tries (it was 2 of 6).
- **Measured today, three live runs on this branch:** 89 of 90 picks right. The three cards that fit no story stayed "none" on 54 of 54. The merge card reached Merge-safe boards on 18 of 18. The swipe card reached Android on 17 of 18, with one "none" (the safe miss). No card went to a wrong board.
- **Suite:** `pwsh -File tools/test.ps1` passed, 962 unit and 263 integration. After rebasing onto two new WOR-0216 commits, the unit tests (962) and `BoardTabsTests` (8) passed again.

**To check this round:** in `D:/GitHub/ThePhilipWilson/Darg` on `android`, run `$env:DARG_LIVE='1'; dotnet test tests/Darg.Core.Tests --filter A_live_model_moves` two or three times (about 30 s each). Every run should pass. The WOR-0218, WOR-0216 and WOR-0217 cards should be `none` on every try, and the merge and swipe cards on their stories on nearly every try. The menu and default board haven't changed since QA passed them (steps below).

**What changed before** (commit 5f86956 on `android`, pushed):
- Right-click a board tab: a menu with **Set as default**, with a ✓ by it on the board that's the default now. Picking it keeps the board in `default-board` beside `shown-boards` in the project's `.darg/`. New cards (Fettle's new-card, a note's ticket, + Card on All) land there. With none set, or once that board is gone or renamed, it goes back to Main by name.
- Haiku's placing prompt is strict. A card moves off the default board only when it is plainly part of one story; a shared word, a near fit or "not sure" means it stays. Each board's name now sits on its own `Board:` line. With the old layout Haiku sometimes answered with a card's title, so an obvious card like "Three-way merge of board.json" was never placed.

**Reproducing it first:** on today's boards, WOR-0218, WOR-0216 and WOR-0217 all came back "none" from the old prompt too (3 tries each), so I couldn't reproduce the miss. Fettle's transcript shows WOR-0218 went in through new-card with no `board`, so Haiku placed it, but nothing records where it went. The default board and the stricter prompt guard against it either way.

![the tab's menu, Art set as default](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0219/board-tab-menu-default.png)

**To try it** (after ⟳ Update):
1. On Darg's Board, right-click the **Merge-safe boards** tab. The menu says Set as default, with no ✓. Right-click **Main**: it has the ✓.
2. Pick Set as default on Merge-safe boards. The line under the tabs says it's the default. Ask Fettle for any card: it lands on Merge-safe boards, and Haiku may move it to Main or another story. Restart Darg: the ✓ is still on Merge-safe boards.
3. Set Main back as the default.
4. Live Haiku check: `$env:DARG_LIVE='1'; dotnet test tests/Darg.Core.Tests --filter A_live_model_moves`. Five real-shaped cards: the three that fit no story stay ("none") on 6 of 6 tries; the merge card reaches Merge-safe boards, and the phone swipe card Android, on at least 4 of 6. To see it fail, set `StoryAtLeast` to 7 (more than `Tries`): both story cards fail with their picks listed.

**Look for:** whether the strictness feels right as real cards come in. A card that plainly belongs to a story should now nearly always move there.

## QA notes (2026-11-26)

Checked at b540ee2 (`android`, holds ec05afd) in its own worktree, `.shots/qa-0219`.

- **Passed: full suite** (`pwsh -File tools/test.ps1`): 930 unit and 255 integration tests, no failures. The integration part took 11 min, not the usual 45 s, because another suite (the wor-0216 worktree's) was running at the same time. That is not a fault of this card.
- **Failed: the live test fails most runs today.** `DARG_LIVE=1 dotnet test tests/Darg.Core.Tests --filter A_live_model_moves`, three full runs: run 1 failed with `Three-way merge of board.json conflicts reached Merge-safe boards on fewer than 2 of 6 tries: none, none, none, none, none, none`. Runs 2 and 3 passed (3 of 6 and 2 of 6). The merge card alone, five more runs: it reached Merge-safe boards on 1, 0, 1, 1 and 2 of 6 tries, so 4 of the 5 failed. Over all 8 runs the test failed 5 times, not about 1 in 50 as How to check says.
- **Wrong: the measured rate in How to check.** It says a plainly-story card stays on the default board about 3 tries in 10. Today the merge card stayed on about 7 in 10: 38 of 48 tries in the test runs, 8 of 12 through `Haiku.Ask` from a scratch program, and 4 of 12 through bare `claude -p` with the same args. That is 50 of 72 overall. Every "none" was Haiku answering `None`. None was a reply `Pick` refused, and no call failed or timed out (raw replies printed through the app's own `Haiku.Ask`; one `Board: Merge-safe boards` reply was correctly unwrapped). At a 30% move rate, "at least 2 of 6" fails about 4 runs in 10, which matches what happened. So the Done-when line "a card plainly about a story, such as three-way merge of board.json conflicts, still goes to Merge-safe boards" doesn't hold for most tries today.
- **Passed: the other cards.** The Android swipe card reached Android on 17 of 18 tries and went nowhere else. The three cards that fit no story (WOR-0218, WOR-0217 and WOR-0216's text) stayed "none" on 54 of 54 tries. The strictness against wrong boards holds. What fails is that the plainly-story merge card mostly stays.
- **Not re-checked:** the menu, `default-board` and new-card landing on the default across a restart. How to check says the app and prompt are unchanged since the last QA passed them, and the suite's `Set_as_default_on_a_tab_makes_new_cards_land_there` passed again.
- **Next step is probably Philip's call, not another threshold.** The decision he picked ("keep the strict prompt, fix the test") assumed a story card stays about 1 try in 10. The second round measured 3 in 10, and today it is about 7 in 10. No try count fixes a test whose expected card moves 3 tries in 10. The options are on the card already: loosen the prompt (the looser variant moved the Story cards card to the wrong board 3 in 10), or ask Haiku more than once and move on any story pick when none names another board. At a 30% move rate, 3 asks still leave about a third of plainly-story cards on the default board. Ask him with a Decision before building either.

## QA 2026-11-25

Checked at c7189d1 (`android`, which has 5f86956) in a worktree of its own (`.shots/qa-0219`).

- **Full suite** (`pwsh -File tools/test.ps1`): passed, 930 unit and 254 integration tests, no failures.
- **The integration test can fail:** with `InboxName` made to ignore the default, `Set_as_default_on_a_tab_makes_new_cards_land_there` fails. With the code put back, it passes.
- **Real window** (debug build on a throwaway instance with fake claude, driven by `.shots/qa-0219/.shots/qa-live.ps1`: UI Automation finds the tabs and a real right-click opens the menu): with nothing set, Main's menu has Set as default ✓ and Art's has no ✓. Set as default on Art writes `default-board` (`Art`) in the repo's `.darg/`, and the line under the tabs says Art is the default. new-card then lands on Art. After closing and restarting Darg, Art is still ticked and new-card still lands on Art. Setting Main back puts new-card on Main again. The API's `boards` marks Art `inbox: true`. No crash.log. Screenshots: `.shots/wor-0219/qa-main-menu-none-set.png`, `.shots/wor-0219/qa-art-menu-after-restart.png`.
- **Live Haiku** (`DARG_LIVE=1`, `A_live_model_moves_only_cards_plainly_part_of_a_story`): the WOR-0218, WOR-0217 and WOR-0216 cards got "none" on 3 of 3 tries, and the merge card got Merge-safe boards on 3 of 3. The Android swipe card got **none on its first try**, so the test failed, then Android on 9 of 9 tries over three reruns. Since the test needs all 3 tries to agree, about 1 miss in 10 makes it fail roughly one run in four. The miss leaves the card on the default board, the safe side. See the Decision above.
- **Can't be automated:** whether the strictness feels right as real cards come in ("too many staying on Main"). That needs weeks of real cards and Philip's sense of where they belong; a test can only check examples someone has already labelled.
- Minor, not a fault: the context menu opens at the pointer and covers the tab names while it's open. That's Avalonia's usual placement.
