# WOR-0144: Darg keeps a project's working files in a .darg folder in its repo, and finds docs in the repo first

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** darg, docs
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

Decided on WOR-0123 (2026-11-18): a project's human-read docs live in `docs/` at the top of its first repo; Obsidian is no longer how they're read (Darg's Docs section is), so no junction back into the vault; Darg's working records go in a `.darg/` folder in the repo, ignored by git unless a project opts in; boards and notes already in `vault/the board app` stay there. The proposal: Artifacts, `docs-in-repos.html` under WOR-0123's agent.

Two code changes in Darg:

1. **Docs detection** (`ProjectDetect`): a repo's `docs/` wins over the vault's `Code/<name>`, the other way round from today. Update `ProjectDetectTests`.
2. **`.darg/` in the first repo** for what Darg writes per project and now keeps in `%APPDATA%/Workbench/<instance>/projects/<id>/`: `reports/`, `mistakes/`, `artifacts/`, the project's `flows/`, and the board (`tasks/`) and `notes/` when Darg made them (Claude Draw's and Darg's own; boards configured elsewhere, like `vault/the board app`, stay put). A project with no repo keeps the old folder.
   - Ignored by default through `.git/info/exclude` (no commit lands in a public repo); a project opts in by deleting that line and committing `.darg/`. A setting or a Setup checkbox for that is a nice-to-have, not needed.
   - Agents work in worktrees, but `.darg/` lives in the main checkout only: keep giving them absolute paths (`--add-dir`, the report path in card prompts), never a path relative to their cwd.
   - Move the existing folders for every project on first start with the new code (not while the app runs: live agents hold the old paths), and say what moved in the status line.
   - The global unlogged-mistake Stop hook counts writes by file name (`mistake_log.md`), so it should still work; check.
   - `DocLinks` searches the Obsidian vault above the docs folder for a `[[Name]]` it can't find; in a repo there is none, so such links show as missing. That's expected now.
   - Integration test: a project whose repo exists gets its report written under `<repo>/.darg/reports/`, and `.darg/` is in `.git/info/exclude`; it fails without the change.

## How to check

Changed (commit 99862d4 on main; branch wor-0144 merged and removed): `ProjectData.Root` (Core) decides where reports, mistakes, artifacts and a project's flows live: `<first repo here>/.darg`, else the old `<instance>/projects/<id>`. `.darg/` goes in `.git/info/exclude` when first made. `ProjectData.Migrate` runs at the start of `MainViewModel`, moves each project's old folder into `.darg` (repointing boards and notes it held, rewriting saved agents' paths), and the footer says what moved. `ProjectDetect` takes a repo's `docs/` before the vault's `Code/<name>`, and a new project's board goes in `<repo>/.darg/tasks`.

Try it (only exercised on throwaway instances, not yours):
1. Optionally back up `%APPDATA%/Workbench/home/projects`, then start Darg with the new code (`pwsh -File tools/publish.ps1` from a terminal, or a debug build).
2. The footer should read "Moved projects' working files into .darg in their repos: ..." until you pick another project.
3. Open `.darg` in Darg's repo (and the other projects' repos): the old reports, mistakes and artifacts are there; `git status` shows nothing new; `.git/info/exclude` ends with `.darg/`.
4. Reports, Mistakes and Artifacts still list everything. A project whose boards are in `vault/the board app` keeps them there. Darg's own board and notes (in the instance folder) move too on that first start.

Tests: `pwsh -File tools/test.ps1` (unit 595, integration 49, all pass). New: `ProjectDataTests`, `DargFolderTests`.

## QA 2026-11-18

Passed, on main at a3d0d16 (includes 99862d4):
- `tools/test.ps1`: unit 610/610, integration 56/56, exit 0.
- The migration on a mirror of the real instance (`.shots/qa-0144`: the real `projects/` and `agents/` copied, every repo replaced by a throwaway git repo, the board app boards by copies), run by a Release build with fake claude. Every configured project's files moved with the counts unchanged (Workbench 461 files, Claude Draw 4, the rest 1 to 2). Workbench's and Claude Draw's boards were repointed into `.darg/tasks`; the the board app boards stayed. `.darg/` was added to each first repo's `.git/info/exclude` (Workspace, a second repo, left alone). `git status` was clean in each repo. Saved agents' paths were rewritten; the one old path left is quoted text in a transcript.
- The footer read "Moved projects' working files into .darg in their repos: Alpha (3), … Workbench (5)." (read with UI Automation). Home, Reports (85), Mistakes (22), Notes and Artifacts all listed from `.darg`. A new card and its card agent got absolute `.darg` paths (`--add-dir`, card file, artifacts folder). A second start moved nothing and showed the normal footer.
- The unlogged-mistake hook matches `mistake_log\.md$` on the path, so it still fires.

Notes: the footer counts are top-level folders, not files. `Delta` in `projects/` isn't a configured project, so it stays. Site's old tasks and notes (not in use) go to the Site repo's `.darg`, which git ignores. The real migration runs on your next publish.
