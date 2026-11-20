# Using Darg, section by section

Each project is a tab across the top (the tab bar is also the title bar; drag it to move the window). Each project's rail lists the sections that project has switched on in Setup. The header carries the usage bar, `today …` timer, 🎨 Settings, 🔧 Tools, ⟳ Update/Restart and Setup. Fettle sits in the corner.

## Home

Every project opens here. Tiles for cards in the board's middle columns, agents working or needing you (refreshed every 3 s), the newest artifacts and the last commits, above the README (the first repo's, then the others', then the docs folder's README or index.md). The README shows its pictures, including badges and HTML `<img>` with width and `align="center"`. ✎ Edit opens it in Docs; with no README an agent can draft one.

## Board

Jira-style boards in markdown. **Each board is a story**; one is the inbox (named Inbox, Main or Backlog, else the first). A note's ticket and + Card land in the inbox, and then Haiku moves the card to the story board it fits (from each board's name and newest 15 titles; its reply is refused unless it is exactly one board's name; a card picked up meanwhile stays) (WOR-0087).

- **Board tabs** (WOR-0149): one tab per board. A plain click shows only that board; Ctrl+click adds it or takes it off (never the last). Extra boards show as stacked rows (three share the height, more scroll, 320 px minimum); filter, chips, Show completed and Work on N apply to every row; dragging a card to another row moves it to that board and keeps its id.
- **Ticket quality star** (WOR-0156): beside every card's id, a star out of 3, a point each for saying what's wanted (20 words or more), where it lives (a `Where:` label or heading, a file or path, code in backticks) and when it's done (a `Done when:` label or heading, acceptance, expected, repro steps or a checklist). Red at 0 or 1, amber at 2, green at 3; the tooltip says what's missing. `TicketQuality` in Core reads the description and the sections after it, but not How to check, QA notes or a Decision. It follows the file, so ✦ Improve (whose prompt carries the score and asks for those labels) moves it.
- **Filter and chips**: by text and label. Cards have a type (Task, Bug, Story…), guessed from the text if not given (`CardTypes.Guess`, which also catches bugs written as prose).
- **Column ⋯ menu**: rename, minimize (stored as `"minimized"` in `board.json`), remove (the cards go to the first column), sort: Manual, A-Z, Newest or Longest in column, plus Reverse (WOR-0146). The sort is a view order kept in `column-sorts`; a drag inside a sorted column is refused with a reason. A card gets a `Moved:` line whenever its column changes (also stamped when a Status is edited outside Darg).
- **Default columns** include **For Human Attention** (renamed from "Needs Phil Attention", WOR-0120); boards without one gain one before Done, once (`attentionColumn: true` in `board.json`, so a deleted column stays deleted).
- **Done column ⋯ → Wrap up into stories…**: an agent groups Done cards into stories and asks first. Each story gets a doc under `Stories/` and a Story card in Done; the members go to Completed (hidden unless Show completed is ticked, files kept) (WOR-0074).
- **Card panel** (click a card): edit title, description and labels; ▶ Work on this, ✦ Improve this card, 🧪 Run QA, ↩ Send back, ✓ Done and 🔔 For human attention (on a card in QA), ✓ Mark done (moves any open card to the last column), 📋 Report. A card in Done offers none of the agent actions. A `## Decision` shows at the top with its options. The ✕ top left closes without saving.
- **Bulk**: ▶ Work on N / ✦ Improve N on picked cards; ▶ Work All (n) at the top of the backlog, a To Do column and the working column; 🧪 QA Run All (n) on QA. Above 3 agents the column asks "Start n agents?" first. A card that fails to start is named in the status line and the rest start.

See [cards-and-qa.md](cards-and-qa.md) for what those buttons do together.

## Notes

The post-it wall: one `.md` per note with Obsidian properties, columns in `wall.json`. Drag notes between columns, drag column headers to reorder (Ticketed and Archive stay put). **Make it a ticket** creates a card in the inbox: the title is the first short sentence or the first words plus "…" and the description keeps the whole note (`NoteStore.TicketText`); Haiku then retitles it in the background (reply refused unless it is one short line in the note's own words; a card you renamed meanwhile is left alone). A note with a ticket shows in a Ticketed column; once its card is Completed it moves to `notes/archive/` and comes back if the card reopens (Archive (n) in the toolbar shows them; notes aren't archived by hand). Polaroid notes show a square crop on the wall and the whole picture when opened.

## Docs

The project's docs folder as a tree that expands (a folder of docs is a tree, never a flat list), rendered as markdown with a finder. `[[wikilinks]]` open the doc they name. Edit, then Save or Ctrl+S. Pictures resolve relative to the open doc's folder.

## Agents

All the project's agents with a transcript that draws what Claude Code does: markdown replies, images, diffs, write previews, questions and permission prompts inline. Tabs above the list: All plus one per board; click filters, Ctrl+click combines, agents with no card show under All (WOR-0150). Header shows the model profile picked and why; a card agent's header has ↗ WOR-xxxx to show its card. A "!" pip on the rail and the project tab means an agent needs you. Agents are saved after every turn and resume after a restart; × forgets one. Dictate with the mic by the message box. The Agents box and the Terminal toolbar show a repo picker when the project has several repos.

## Flows

Multi-agent pipelines, one markdown file each: `# Name`, `Max rounds: n`, a `## Role` section per stage. A stage whose brief starts `Sends back to: Role` is a checker ending with `VERDICT: PASS|FAIL`; any stage can end with `FLOW: STOP`. Shared flows are in the instance's `flows/`; a project's own are in its `.darg/flows/` (shown as "Name · this project"). Run one from a card (context menu, panel) or the Flows section; each stage is an agent handed the previous stages' last reply, fully automatic, and a run lasts only while the app does. The **+** generates a new flow from what you type (basic or art, this project or shared); an art flow follows the project's art skill by name, uses Forge and ends in an image review. The starter `designer-engineer-qa.md` is written the first time.

## Reports

Finished-agent reports, newest first, unread dotted and counted on the rail. A kind filter, shown as tabs, each with its count: All reports, Finished tasks, Investigations (including CNR), QA reports. Mark unread, Mark all read, Unread only, Show card, commits link to GitHub. **Clean up** archives QA reports of Done cards and writes any missing done report. **Story** dropdown + Archive story moves finished reports to `reports/archive/<story>/`; Archive (n) browses them, Restore brings them back (QA and undecided decision reports are never archived). A pending decision report offers ✓ Approve and ↩ Send back. Reports show markdown pictures and links.

## Artifacts

Pages agents made, shown in a web view. See [architecture.md](architecture.md#artifacts) for how they're served and how `darg.send()` and image reviews work. A native web view is a native window, so nothing Avalonia draws (Fettle, popups) shows over it.

## Mistakes

Each project's `mistake_log.md`, newest first, with category counts; a category at three entries is marked red, because three of a kind is when a runtime gate beats another rule. Darg only reads it; agents write it (every project agent's briefing tells it to log corrections). ✗ Mistake beside ✎ Draw tells the agent to log its last reply. The file name matters: the global unlogged-mistake hook matches `mistake_log.md`.

## Context

(☰) What an agent in this project starts with: the `CLAUDE.md` and `CLAUDE.local.md` files Claude Code loads (user's, each folder down to the repo, other repos'), the project's Claude memory files and Darg's default context. The picked file is drawn as markdown; Open sends it to its app. The user's `~/.claude/philip/*` detail files and skills are not listed (a choice, WOR-0145).

## Code

Each repo as a tree of git's file list, a finder, Changed only, tabs, syntax colouring (TextMate), git status. Editing is supported: type, ● marks an edited tab, Ctrl+S saves, closing an edited tab warns once. The editor can't be driven through Windows' accessibility interface, so typing in it has only been tried by hand.

## GitHub

Per repo, through `gh` with the project's own account's token (set in `GH_TOKEN` for that call; the active gh account is never switched, and a private repo seen with the wrong account answers 404): open PRs, issues (one click makes a card) and recent CI runs. GitLab sits beside it and needs `glab` installed (untested here). Both are read-only; the agent runs all git and Darg never merges.

## Preview (the Site section)

A web view of the project's dev server: `preview` in `instance.json`, else the first repo with a `dev` script in `package.json`. The server runs in its own job so it stops with the app.

## Terminal

Consoles as tabs. `Shells` finds PowerShell 7, Windows PowerShell, Git Bash, cmd and each WSL distro (but not Docker Desktop's); + Shell opens the instance's `shell` and picking one from ▾ opens it and makes it the default. Saved commands (set in Setup as `Name: command` lines) show as ▶ buttons and always run in the platform's first shell (PowerShell on Windows). A project's `commits` rule maps a folder to `push`, `commit`, `ask` or `never`; the deepest folder holding the work wins, and agents are told the rule.

## Tools (🔧)

Status and start for Forge, ComfyUI, Ollama and Pinokio; Forge draws from a prompt, Ollama answers with a chat model, ComfyUI's newest images show. Start launches through the shell so the tool outlives Darg. Ollama's 14B models don't fit on the GPU while Forge holds SDXL; 7B ones do.

## Settings (🎨), Setup, Fettle

- **Settings**: a card per theme (Dark, Light, Girlypop), saved in `settings.json`.
- **Setup**: the instance's projects, per-project features as tick boxes, folders, repos, saved commands, commit rules, boards and docs. Saving rebuilds only the projects that changed.
- **Fettle**: Chat and Notifications are tabs under its header; a count pip shows when it is closed. Tool steps are single dim lines with output folded. Ask for a described change ("X should do Y") and it makes a Backlog card; "just fix it" edits. When you are looking at an agent it asks "New ticket" or "Amend the agent's task" first.
