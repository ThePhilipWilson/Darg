---
kind: feature
status: agreed
parent: seamless-machine-switching
board: Merge-safe boards
---
# Merge-safe boards

Boards that two machines can both change between syncs and still merge cleanly: a card's next id comes from the card files, its place in a column is a Rank line in its own file, and a card that two machines both numbered WOR-0200 is renumbered on this side before the pull merges, references and all.

## Goals
- Make and move cards on the PC and the Mac on the same day and never see two cards with one id, or a card that has vanished or moved back.
- Drag cards about on both machines without a pull ever stopping on board.json.
- When a card had to be renumbered, be told which one and what it is now, and find everything that pointed at it (its blockers, its reports, its note, its plan row) pointing at the new id.

## Not doing
- Running pulls, pushes or commits, the `darg` branch, the footer sync line or the Sync panel: those are [.darg on its own branch](darg-on-its-own-branch.md) and [Auto-sync](auto-sync.md). This plan gives them the calls to make (`BoardSync`, see Design) and tests those calls on throwaway git repos.
- Syncing around making a card, and the one helper every card-making path goes through: that is Auto-sync's "Sync around making a card, and on close".
- Conflicts in a card's text (the same card edited on both machines): the Sync panel keeps both sides, as the parent plan settles.
- Restoring the Main board's WOR prefix, next id and columns after the DAR-0003 cherry-pick: that is part of [.darg on its own branch](darg-on-its-own-branch.md).
- Renumbering a card already pushed. Only a card the other machine has never seen changes id.
- Rewriting ids in commit messages or anything else already pushed to main.
- Column sorts (A-Z, Newest, Longest): they stay a per-machine view over the manual order, as now.

## Questions

### Should Darg stop writing `order` and `nextId` in board.json?
- **Stop on every board** (recommended): order lives only in the Rank lines and the next id only in the card files, so board.json changes only when columns, the prefix or folded lists do. One code path. The third-party board app, reading a board Darg has written since, sorts cards its own way and may number a new card from a stale `nextId`.
- **Stop only on boards in a synced `.darg`**: boards kept elsewhere (another instance's vault) keep both fields up to date for the third-party app, at the cost of two ways of writing a board.
- **Keep writing both as a mirror**: every move and every new card still rewrites board.json, and Darg settles the conflicts on `order` and `nextId` itself on every pull (take the highest `nextId`, rebuild `order` from the ranks).

**Context:** the parent plan moves the vault's boards into each repo's `.darg`, so the third-party app no longer reads those. Two machines that each make a card both rewrite `nextId`, which is a merge conflict whenever they made a different number of cards.

**Answer:** Stop only on boards in a synced `.darg` (Philip, 2026-11-24)

### A card that has to be renumbered has an agent at work on it. What happens?
- **Renumber at once and tell the agent** (recommended): the pull goes ahead, and the agent gets a queued message ("your card WOR-0200 is now WOR-0201; use that id for its report and branch") that it reads when its turn ends.
- **Hold the pull until the agent's turn ends**: nothing changes under a working agent, but that project's boards don't take the other machine's changes for as long as the turn lasts, which can be an hour.

**Context:** card agents are told their card's id in the prompt and write it into reports, QA notes and their branch name. Renumbering is rare: both machines have to make a card while apart.

**Answer:** Renumber at once and tell the agent (Philip, 2026-11-24)

### Where does a card land when it enters a column with no place given (an agent or a hand edit set its Status)?
- **At the bottom, oldest arrival first** (recommended): as today, where agents append the id to the end of the column's order.
- **At the top, newest arrival first**: what an agent just moved into QA or For Human Attention is the first thing seen in that column.

**Context:** once agents stop editing board.json, they move a card by its Status line alone and take its Rank line out, so Darg places it by when it moved.

**Answer:** At the top, newest arrival first (Philip, 2026-11-24)

### Auto-sync's draft checks for clashes after the merge. Should its round call this feature before the merge instead?
- **Before the merge** (recommended): Auto-sync's round calls `BoardSync.BeforeMerge` between fetch and merge, and hands a board.json conflict to `BoardJsonMerge` before keeping both sides. Every mention of WOR-0200 on this machine then still means this machine's card, so renumbering rewrites them all safely. Auto-sync's plan gains those two lines in its round.
- **After the merge, on Auto-sync's `Pulled` event**: Auto-sync stays as drafted. Renumbering then has to guess which card a mention means in a file both machines changed, and every board.json conflict lands in the Sync panel as two files for him to pick from.

**Context:** [Auto-sync](auto-sync.md)'s round (step 6) raises `Pulled` after the merge "for Merge-safe boards' id check", and its both-sides keeping covers board.json. The two plans were drafted side by side.

**Answer:** Before the merge (Philip, 2026-11-24)

## Design

### What it builds on
- A card's column already comes from its Status line, and its file already carries Created, Done and Moved lines (`BoardStore` in `src/Darg.Core/Board.cs`). Field lines Darg doesn't know are kept as written, and the third-party board app ignores ones it doesn't know.
- `BoardStore.Create` already numbers past every card file on the boards beside it with the same prefix (`HighestUsed`), so the next id is mostly there.
- `CardBranch` already lets a card name its branch with a `Branch:` line, which is how a renumbered card keeps a branch that was already pushed.
- `BoardViewModel` already reloads on any board file changing, so a pull that changes card files redraws the board with no new code.

### Next id
The next id for a prefix is the highest number with that prefix on any card in the project's boards folder, plus one: every board's `items/` and `.deleted/` (a deleted card's id is never handed out again, since reports and notes may still name it), whatever prefix the board itself has (a WOR card moved to a board with another prefix still counts). `nextId` in board.json, where present, is a floor only. `BoardStore.NextId(boardsFolder, prefix)` replaces `HighestUsed`, and `Create`, `CreateBoard` and `Transfer` use it. Whether board.json is still written on create is the first Question.

### Rank: a card's place in its column
- A card's place is a `- **Rank:** a0V` line among its properties (after Moved). A rank is a fractional-index key: a short string of digits and letters that sorts by plain ordinal comparison, and there is always a key between any two (`CardRank.Between(a, b)`, also `Before`, `After`, and `Spread(n)` for n evenly spaced keys). Moving a card writes only that card's file, and never renumbers the others.
- `BoardStore.Load` sorts each list by Rank, ties by card number. A card with no Rank goes after the ranked ones, ordered by its Moved line (the third Question may turn that to before).
- `Move(folder, id, list, beforeId)` sets Status and a Rank between the card before `beforeId` and `beforeId` (or after the last card). `Create` gives the new card a Rank after the backlog's last; `Transfer` one at the end of the target list; `RemoveColumn` ranks the cards it sends to the backlog after the backlog's last. `Delete` no longer touches board.json.
- Two machines that both put a card between the same two get the same key. The tie sorts by number, which is a stable order on both machines, and the next drag fixes it.
- Agents moving a card by hand set its Status line and take its Rank line out (see Agents' instructions), so it lands at the bottom (or the top) of its new column rather than at its old column's place.

### Bringing existing boards over
The first load of a board that has an `order` and cards without Rank lines writes ranks from `order` (`Spread`, so the same order gives the same keys), then takes `order` out of board.json (subject to the first Question). The keys depend only on the order, so the PC and the Mac each migrating the same board write identical files, which git merges without a conflict. Cards not in `order` are ranked after the rest by number, as they show today.

### board.json merges
With order and the next id gone, board.json changes only when columns, the prefix or the folded lists change. `BoardJsonMerge.Merge(base, ours, theirs)`, a pure function, merges two machines' board.json three ways: columns by id (an add on either side is kept, a removal on either side removes, a rename on one side wins, the same column renamed differently on both takes theirs and says so), the folded lists by id, and any other key from the side that changed it (theirs when both did). Since a card's column is its Status line, cards that still name a column's old name after a merged rename have their Status lines rewritten to the new one, so they don't fall into Unsorted. [Auto-sync](auto-sync.md)'s conflict keeping calls it on a board.json conflict before keeping both sides, and only a case it can't settle reaches the Sync panel (the fourth Question).

### Id clashes: settled between fetch and merge
- The clash check runs after `git fetch` and before the merge (`BoardSync.BeforeMerge(repo, fetchedRef)`), not after, if the fourth Question goes that way. At that point every reference on this machine to WOR-0200 means this machine's card, because the other machine's WOR-0200 hasn't arrived yet, so renumbering can rewrite every occurrence safely. After the merge, the same text could mean either card.
- A clash is a card id that the fetched `darg` tree has and that this machine also has on a card the remote doesn't (a file added in a local commit since the last push, or not committed yet). The local card gets the next id past both sides' highest. The fetched card keeps its id, as the parent plan settles.
- Renumbering (`CardRenumber.Apply(project, from, to)`) rewrites, in this machine's files only:
  - the card's heading and file name, plus a `- **Renumbered from:** WOR-0200 (date)` line so an old mention (a chat, a commit message already on main) can be traced;
  - `Blocked by` lines, `card:` links and bare ids on every board, in notes (`ticket:`), reports (the `card:` property and file names) and mistake logs in `.darg`;
  - plan rows in `docs/design` whose heading is that id with that card's title (a row the other machine made has another title and is left alone);
  - saved agents' card ids, and the card's branch: renamed if it was never pushed, else kept with a `Branch:` line added to the card so its agent carries on there;
  - push keys and the notification id for a waiting decision.
  Then it commits ("Darg: WOR-0200 renumbered to WOR-0202, clash with the other machine's WOR-0200"), and the merge goes ahead. The status line names both ids. A live agent on the card is handled as the second Question settles.
- As a fallback, a board read that finds two card files with one id (made outside Darg's sync: by hand, by the third-party app) marks both tiles with a Warn chip and says so in the status line, with a Renumber action on the newer one that runs the same `CardRenumber`.

### Syncing around a new card
The parent plan settles that Darg syncs right before and after making a card. [Auto-sync](auto-sync.md) builds that, with one helper every card-making path goes through. This plan's part is that the helper ends in `BoardStore.Create`, which takes the next id from the card files after the pull, and that agents make cards through the API's `new-card` (below) rather than writing files, so they go through the helper too.

### Agents' instructions
Prompts now tell agents to edit board.json's `order` and work out `nextId` themselves (`ReportStore.Rules`, `ReportStore.CheckPrompt`, `Stories.Prompt`, the board-sorting agent in `BoardViewModel`, Fettle's briefing in `MascotViewModel`, `CardDecision.Rules`). They change to: move a card by its Status line and take out its Rank line (Darg places it); make a card through the API's `new-card`, which gains what a story needs (type, column, Done date), so it goes through the sync and takes a free id. A prompt that wants a card at a given place (a story at the top of Done) carries a Rank Darg works out when it writes the prompt.

### Risks
- **Agents with old habits.** An agent that still writes `order` into board.json makes a field Darg no longer reads, which is harmless; one that picks an id by hand can clash with an id already used, which the duplicate-id fallback catches.
- **The third-party board app.** If it still opens a board, it sees cards in its own order and may hand out a used id. The first Question covers it.
- **The order of the sync hooks.** The clash check only works as designed if Auto-sync's round calls `BeforeMerge` between fetch and merge, which its draft doesn't yet. The fourth Question settles it; until then the calls are tested here on throwaway git repos and nothing in the app calls them.

## Cards

### MER-0001: Next id from every card file in the project
`BoardStore.HighestUsed` only reads boards whose prefix matches and only their `items/` folders, so a WOR card moved to a board with another prefix, or a deleted card in `.deleted/`, can have its id handed out again. Replace it with `BoardStore.NextId(boardsFolder, prefix)`: the highest number with that prefix on any card file in any board's `items/` or `.deleted/` in the boards folder, plus one, with board.json's `nextId` as a floor only. `Create`, `CreateBoard` and `Transfer` use it. Whether `Create` still writes `nextId` back follows the plan's first Question.
**Where:** `src/Darg.Core/Board.cs` (`BoardStore.Create`, `CreateBoard`, `Transfer`, `HighestUsed`), `tests/Darg.Core.Tests`.
**Done when:** unit tests show a new card numbered past a same-prefix card on a board with another prefix, past a card in `.deleted/`, and past board.json's `nextId` when that is higher; the existing board tests still pass.
**Blocked by:** none

### MER-0002: Fractional rank keys
Add `CardRank` in Core: keys that sort by ordinal string comparison and always leave room between two, as fractional indexing does. It needs `Between(a, b)` (either side may be null for the start or end of a list), `Before(a)`, `After(a)`, and `Spread(n)`, which gives n evenly spaced keys that depend only on n, so two machines ranking the same list get the same keys. Keys stay short under repeated inserts at one spot, and a key that isn't valid (typed by hand) is treated as no rank.
**Where:** `src/Darg.Core/CardRank.cs` (new), `tests/Darg.Core.Tests`.
**Done when:** unit tests show `Between` sorts strictly between its arguments for random pairs, 1000 inserts at the front and 1000 between the same two cards keep keys under 20 characters, `Spread` is deterministic and sorted, and an invalid key is reported as none.
**Blocked by:** none

### MER-0003: Card order from Rank lines
Move a card's place in its column out of board.json's `order` and into a `- **Rank:**` line in its own file, so moving a card writes only that card. `BoardStore.Load` sorts each list by Rank (ties by number, cards with no Rank after the ranked ones by their Moved line); `Move` writes Status and a Rank between its new neighbours; `Create`, `Transfer` and `RemoveColumn` rank the cards they place at the end of the list; `Delete` stops touching board.json. Whether `order` is still written as a mirror follows the plan's first Question; where a card with no rank goes follows the third.
**Where:** `src/Darg.Core/Board.cs` (`Load`, `Move`, `Create`, `Transfer`, `RemoveColumn`, `Delete`), `src/Darg.Core/BoardLayout.cs`, `tests/Darg.Core.Tests`, `tests/Darg.Desktop.Tests`.
**Done when:** a unit test drags a card between two others and only that card's file changed (board.json's bytes are the same); an integration test drags a card on the board and after a reload it shows in its new place; the phone's move (`move-card` with a before id) lands in the same place.
**Blocked by:** Fractional rank keys

### MER-0004: Bring existing boards' order into Rank lines
Boards made before Rank lines keep their order in board.json. The first time Darg loads a board with an `order` and cards without a Rank, write each card a Rank from `order` using `CardRank.Spread`, so the same order gives the same keys on every machine and two machines migrating the same board write identical files. Cards not named in `order` rank after the rest by number. Then remove `order` from board.json (unless the plan's first Question keeps it as a mirror). A card that already has a Rank keeps it.
**Where:** `src/Darg.Core/Board.cs` (a `BoardStore.MigrateOrder` called from `Load`), `tests/Darg.Core.Tests`.
**Done when:** a unit test migrates a copy of a real board's files and the shown order is unchanged; migrating two copies of one board gives byte-identical card files; a second load writes nothing.
**Blocked by:** Card order from Rank lines

### MER-0005: Three-way merge of board.json
Two machines can both add, rename, remove or reorder columns, fold lists or change the prefix between syncs, and git's line merge stops on board.json when they do. Add `BoardJsonMerge.Merge(base, ours, theirs)`, a pure function: columns merged by id (adds kept, a removal on either side removes, a rename on one side wins, the same column renamed differently on both takes theirs and reports it), folded lists by id, other keys from whichever side changed them (theirs when both did). With the merged board it returns the cards whose Status still names a renamed column's old name, and `BoardStore.FixStatuses` rewrites them so they don't fall into Unsorted.
**Where:** `src/Darg.Core/BoardJsonMerge.cs` (new), `src/Darg.Core/Board.cs`, `tests/Darg.Core.Tests`.
**Done when:** unit tests cover a column added on each side, one renamed on one side and removed on the other, the same column renamed differently on both (theirs wins, reported), folded lists changed on both, and a card made on one machine in a column the other renamed showing in that column after the merge.
**Blocked by:** none

### MER-0006: Find id clashes between fetch and merge
After `git fetch` and before merging the `darg` branch, find the cards this machine has that the remote doesn't (added in local commits since the remote's tip, or not committed) whose id the fetched tree also uses, and pick each a new id past the highest on either side. This goes in `BoardSync.FindClashes(repo, fetchedRef, boardsFolder)` and only reads: git calls off the UI thread, comparing card ids in the fetched tree's `tasks/*/items` and `.deleted` with the working folder's. `BoardSync.BeforeMerge(repo, fetchedRef)` is the call Auto-sync's round makes between fetch and merge (the plan's fourth Question).
**Where:** `src/Darg.Core/BoardSync.cs` (new), `src/Darg.Core/Repo.cs`, `tests/Darg.Core.Tests`.
**Done when:** a unit test with a bare remote and two clones makes WOR-0200 in both, pushes one, fetches in the other and gets one clash with WOR-0201 as the new id; a card made on one side only, or the same card pushed earlier, gives none; two clashes get two different new ids.
**Blocked by:** Next id from every card file in the project

### MER-0007: Renumber a card and everything that points at it
`CardRenumber.Apply(project, from, to)` changes a card's id on this machine: its heading and file name, a `Renumbered from:` line, `Blocked by` lines, `card:` links and bare ids on every board, in notes' `ticket:`, reports (the `card:` property and file names) and mistake logs; plan rows in `docs/design` whose heading has that id and that card's title; saved agents' card ids; the card's branch (renamed if never pushed, else a `Branch:` line on the card); the push key and notification id of a waiting decision. `BoardSync.BeforeMerge` runs it for each clash, commits the change with a message naming both ids, and says so in the status line. A live agent on the card is handled as the plan's second Question settles.
**Where:** `src/Darg.Core/CardRenumber.cs` (new), `src/Darg.Core/BoardSync.cs`, `src/Darg.Core/Notes.cs`, `src/Darg.Core/Reports.cs`, `src/Darg.Core/Plans.cs`, `src/Darg.Core/AgentStore.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.Push.cs`, `tests/Darg.Core.Tests`, `tests/Darg.Desktop.Tests`.
**Done when:** a unit test renumbers a card that a blocker line, a card link, a note, a report, a plan row, a saved agent and an unpushed branch all name, and each now names the new id while a plan row with the same id but another title is untouched; the two-clone test from Find id clashes ends after the merge with two cards and two ids; an integration test shows the status line naming both.
**Blocked by:** Find id clashes between fetch and merge

### MER-0008: Flag two cards with one id on a board read
A card made outside Darg's sync (by hand, by the third-party board app, by an agent picking an id) can still share an id with another. When a board read finds two card files with one id across the project's boards, mark both tiles with a Warn chip saying "same id as …", say so once in the status line, and give the newer one (by Created, then file time) a Renumber action in its card panel that runs `CardRenumber.Apply` with the next free id. The API's `cards` and `card` carry a `duplicate` flag.
**Where:** `src/Darg.Core/CardIndex.cs`, `src/Darg.Desktop/ViewModels/BoardViewModel.cs`, `src/Darg.Desktop/Views` (the tile and the card panel), `tests/Darg.Desktop.Tests`.
**Done when:** an integration test writes a second WOR-0005 file onto the test board, sees both tiles chipped and the status line saying so, presses Renumber on the newer and sees it with a new id and the chips gone.
**Blocked by:** Renumber a card and everything that points at it

### MER-0009: Agents stop editing board.json
Agents are told to move cards by adding their id to board.json's `order`, and to make cards by working out `nextId` and writing the file themselves. That bypasses the ranks, Auto-sync's pull before a card is made, and the id check. Change every such prompt (`ReportStore.Rules`, `ReportStore.CheckPrompt`, `Stories.Prompt`, the board-sorting agent's prompt in `BoardViewModel`, Fettle's briefing in `MascotViewModel`, `CardDecision.Rules`) to: move a card by its Status line and take out its Rank line; make a card through the API's `new-card`, which gains type, column and Done date for stories. A prompt that wants a place (a story at the top of Done) carries a Rank Darg works out when it writes the prompt.
**Where:** `src/Darg.Core/Reports.cs`, `src/Darg.Core/Stories.cs`, `src/Darg.Core/CardDecision.cs`, `src/Darg.Desktop/ViewModels/BoardViewModel.cs`, `src/Darg.Desktop/ViewModels/MascotViewModel.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`, `tests/Darg.Desktop.Tests`.
**Done when:** a test finds no prompt Darg sends that mentions board.json's `order` or `nextId`; an integration test runs a fake card agent that moves its card to QA by its Status line alone and sees it at the bottom (or top, per the third Question) of QA; `new-card` with type Story and column Done makes a story card there.
**Blocked by:** Card order from Rank lines
