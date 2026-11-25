# WOR-0219: Stricter card placing, and Set as default on a board tab

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 15:11:51

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

## How to check

**What changed** (commit 5f86956 on `android`, pushed):
- Right-click a board tab: a menu with **Set as default**, with a ✓ by it on the board that's the default now. Picking it keeps the board in `default-board` beside `shown-boards` in the project's `.darg/`. New cards (Fettle's new-card, a note's ticket, + Card on All) land there. With none set, or once that board is gone or renamed, it goes back to Main by name.
- Haiku's placing prompt is strict. A card moves off the default board only when it is plainly part of one story; a shared word, a near fit or "not sure" means it stays. Each board's name now sits on its own `Board:` line. With the old layout Haiku sometimes answered with a card's title, so an obvious card like "Three-way merge of board.json" was never placed.

**Reproducing it first:** on today's boards, WOR-0218, WOR-0216 and WOR-0217 all came back "none" from the old prompt too (3 tries each), so I couldn't reproduce the miss. Fettle's transcript shows WOR-0218 went in through new-card with no `board`, so Haiku placed it, but nothing records where it went. The default board and the stricter prompt guard against it either way.

![the tab's menu, Art set as default](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0219/board-tab-menu-default.png)

**To try it** (after ⟳ Update):
1. On Darg's Board, right-click the **Merge-safe boards** tab. The menu says Set as default, with no ✓. Right-click **Main**: it has the ✓.
2. Pick Set as default on Merge-safe boards. The line under the tabs says it's the default. Ask Fettle for any card: it lands on Merge-safe boards, and Haiku may move it to Main or another story. Restart Darg: the ✓ is still on Merge-safe boards.
3. Set Main back as the default.
4. Live Haiku check: `$env:DARG_LIVE='1'; dotnet test tests/Darg.Core.Tests --filter A_live_model_moves`. Five real-shaped cards: three stay ("none"), the merge card goes to Merge-safe boards and the phone swipe card goes to Android, 3 tries out of 3 each.

**Look for:** whether the menu reads right on the real window (I've only seen it in the headless screenshot), and whether the strictness feels right as cards come in: too many staying on Main means it's too strict.
