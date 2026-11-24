# WOR-0152: Artifacts disappear too quickly

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-20 01:48:05

## Description

Philip reports that artifacts in the Artifacts section seem to disappear pretty quickly. Investigate what removes or hides them (expiry, list limit, cleanup on close, refresh) and make them persist.

## Decision answered 2026-11-20 01:48

**Question:** The fix works on your real instance. The one leftover is an identical copy of the old Workbench mistake log in AppData, which the move will never clear. Done as is, or ticket the leftover?
**Asked by:** agent (QA of WOR-0152), 2026-11-20
- **Done, ticket the leftover** (recommended) → done: the card closes; a separate card makes the move delete an old file whose copy in .darg is identical, and say so when the copies differ
- **Done, ignore it** → done: the card closes; the duplicate file stays in AppData, which is harmless
- **Send back** → back: fix the leftover on this card before it closes
**Context:** `%APPDATA%\Workbench\home\projects\workbench\mistakes\mistake_log.md` is byte-identical to `.darg\mistakes\mistake_log.md`. `MoveInto` keeps whichever file is already in `.darg` and never deletes the old one, so the old folder never goes. If the two copies ever differ, the old one is stranded without a word.

**Answer:** Done, ticket the leftover (2026-11-20 01:48)

## QA notes

- 2026-11-20 QA: full suite `tools/test.ps1` on `android` @ bcb41ae (contains ba9a86d): unit 672/672, integration 79/79, build 0 errors. Integration took 7.5 min because other agents were loading the machine.
- The migration has already run on the real instance: instance.json's workbench `boards` is `D:/GitHub/ThePhilipWilson/Workbench/.darg/tasks` and claude-draw's is `.../claude-draw/.darg/tasks`. `.darg/artifacts` holds the 5 old pages (docs-in-repos, wor-0124 decision proposal, wor-0124 round-2 themes, wor-0137 swatches, wor-0143 header) plus PNGs. `.darg/reports` has 211 files (199 plus newer ones) and `.darg/notes` has 113. Main board has 199 lines. Site/.darg exists. `.darg/` is in `.git/info/exclude`.
- Read the new tests (cross-drive move, held file stays and moves next start, held board isn't repointed). They match the claimed behaviour.
- Deviation: `%APPDATA%\Workbench\home\projects\workbench\` is not gone. Its `mistakes\mistake_log.md` (26,586 bytes) is identical to the copy in `.darg`. No file is held open: `MoveInto` skips a file that already exists at the destination and never removes the source, so this folder stays for good. See Decision.
- Not checked by eye: the one-off footer line (it shows once and is gone) and how the Artifacts section looks. The files are where `ArtifactStore` reads them, and the cross-drive test asserts `ArtifactStore.List` returns the moved page.

## How to check

**Cause:** nothing deletes artifacts, and there's no expiry or list limit. WOR-0144 moved each project's working files from `%APPDATA%\Workbench\home\projects\<id>\` into `<repo>\.darg\` on start, using `Directory.Move`, which throws across drives (C: to D:). The exception was swallowed, so nothing ever moved, while Artifacts and Reports switched to reading the empty `.darg`. Everything made before 18 Nov ~14:30 vanished from view: for Workbench, 5 artifact pages (docs-in-repos, the WOR-0124 decision and theme pages, WOR-0137 swatches, WOR-0143 header), 199 reports and 113 notes, with its board still in AppData. No artifact has been written into `.darg/artifacts` since (all 8 agent folders there are empty).

**Fix:** commit `ba9a86d` on `android` (pushed). `ProjectData.MoveInto` goes file by file when a folder is on another drive. A file held open stays behind for the next start and the rest still move. A board or notes folder is repointed only once all of it has moved.

**To try:** update the copy you use (header ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal) and restart Darg. Then look for:
- The footer says "Moved projects' working files into .darg in their repos: Workbench (n), …" once.
- Artifacts lists the 5 old pages under their agents ("an agent no longer listed").
- Reports shows the old reports again (199 files including archive).
- `D:\GitHub\ThePhilipWilson\Workbench\.darg\` now holds `tasks\` and `notes\`, and the Workbench board still shows every card (instance.json's `boards` now points at `.darg/tasks`). The same goes for claude-draw's board, and Site's leftover file.
- `%APPDATA%\Workbench\home\projects\workbench\` is gone. One exception: if a file there was held open, it stays and moves next start.

**Heads-up:** this is the first time WOR-0144's move actually runs on your instance, so the Workbench and claude-draw boards and notes move from AppData into each repo's `.darg` (git-ignored). That's what WOR-0144 intended, but it hasn't happened before.
