# Using Darg, section by section

What each section of the app is for and how to use it, in the order the rail lists them, then the header's pages and Fettle. How the parts work underneath is in [architecture.md](architecture.md); what happens to a card as it moves is in [cards-and-qa.md](cards-and-qa.md).

Each project is a tab across the top (the tab bar is also the title bar; drag it to move the window). Each project's rail lists the sections that project has switched on in [Setup](#setup). The header carries the usage bar, 🍃 Optimise tokens, the `today …` timer, 🎨 Settings, 🔧 Tools, the ⟳ menu (Update or Restart) and Setup ([architecture.md](architecture.md#things-that-run-beside-the-app)). Fettle sits in the corner.

## Home

Every project opens here. Tiles for cards in the board's middle columns, agents working or needing you (refreshed every 3 s), the newest artifacts and the last commits, above the README (the first repo's, then the others', then the docs folder's README or index.md). The README shows its pictures, including badges and HTML `<img>` with width and `align="center"`. ✎ Edit opens it in [Docs](#docs); with no README an agent can draft one.

## Board

Jira-style boards in markdown. **Each board is a story**; one is the inbox (named Inbox, Main or Backlog, else the first). A note's ticket and + Card land in the inbox, and then Haiku moves the card to the story board it fits, from each board's name and newest 15 titles; its reply is refused unless it is exactly one board's name, and a card picked up meanwhile stays (WOR-0087).

- **Board tabs** (WOR-0149): one tab per board. A plain click shows only that board; Ctrl+click adds it or takes it off (never the last). Extra boards show as stacked rows (three share the height, more scroll, 320 px minimum); filter, chips, Show completed and Work on N apply to every row; dragging a card to another row moves it to that board and keeps its id.
- **Ticket quality star** (WOR-0156): beside every card's id, a star out of 3, a point each for saying what's wanted (20 words or more), where it lives (a `Where:` label or heading, a file or path, code in backticks) and when it's done (a `Done when:` label or heading, acceptance, expected, repro steps or a checklist). Red at 0 or 1, amber at 2, green at 3; the tooltip says what's missing. `TicketQuality` in Core reads the description and the sections after it, but not How to check, QA notes or a Decision. It follows the file, so ✦ Improve (whose prompt carries the score and asks for those labels) moves it.
- **Filter and chips**: by text and label. Cards have a type (Task, Bug, Story…), guessed from the text if not given (`CardTypes.Guess`, which also catches bugs written as prose).
- **Column ⋯ menu** (WOR-0146): rename, minimize (stored as `"minimized"` in `board.json`), remove (the cards go to the first column), sort: Manual, A-Z, Newest or Longest in column, plus Reverse. The sort is a view order kept in `column-sorts`; a drag inside a sorted column is refused with a reason. A card gets a `Moved:` line whenever its column changes (also stamped when a Status is edited outside Darg).
- **Default columns** (WOR-0120): they include For Human Attention (renamed from "Needs Phil Attention"); boards without one gain one before Done, once (`attentionColumn: true` in `board.json`, so a deleted column stays deleted).
- **Wrap up into stories** (WOR-0074): the Done column's ⋯ menu starts an agent that groups Done cards into stories and asks first. Each story gets a doc under `Stories/` and a Story card in Done; the members go to Completed (hidden unless Show completed is ticked, files kept).
- **Card panel**: click a card to edit its title, description and labels. Its buttons: ▶ Work on this, ✦ Improve this card, 🧪 Run QA, ↩ Send back, ✓ Done and 🔔 For human attention (on a card in QA), ✓ Mark done (moves any open card to the last column), 📋 Report. A card in Done offers none of the agent actions. A `## Decision` shows at the top with its options. The ✕ top left closes without saving.
- **Card links** (WOR-0158): a card id in Fettle's replies, agent transcripts, docs and reports is a link that opens the card's panel ([architecture.md](architecture.md#card-links)).
- **Bulk** (WOR-0125): ▶ Work on N and ✦ Improve N act on picked cards; ▶ Work All (n) sits at the top of the backlog, a To Do column and the working column; 🧪 QA Run All (n) on QA. Above 3 agents the column asks "Start n agents?" first. A card that fails to start is named in the status line and the rest start.

What those buttons do together is in [cards-and-qa.md](cards-and-qa.md).

## Notes

The post-it wall: one `.md` per note with Obsidian properties, columns in `wall.json`. Drag notes between columns, drag column headers to reorder (Ticketed and Archive stay put).

- **Make it a ticket**: creates a card in the inbox. The title is the first short sentence or the first words plus "…" and the description keeps the whole note (`NoteStore.TicketText`). Haiku then retitles it in the background; its reply is refused unless it is one short line in the note's own words, and a card you renamed meanwhile is left alone (WOR-0082).
- **Ticketed and archived notes**: a note with a ticket shows in a Ticketed column. Once its card is Completed it moves to `notes/archive/`, and comes back if the card reopens. Archive (n) in the toolbar shows them; notes aren't archived by hand.
- **Polaroids**: a polaroid note shows a square crop on the wall and the whole picture when opened.

## Docs

The project's docs folder as a tree that expands (a folder of docs is a tree, never a flat list), drawn as markdown with a finder. Links between docs open the doc they name: relative markdown links (`[guide](guide.md)`, opened at the doc rather than the heading) and `[[wikilinks]]`. Card ids link to their cards. Edit, then Save or Ctrl+S. Pictures resolve relative to the open doc's folder. These docs follow the rules at the end of [README.md](README.md#how-these-docs-are-written).

## Agents

All the project's agents with a transcript that draws what Claude Code does: markdown replies, images, diffs, write previews, questions and permission prompts inline. How agents run is in [architecture.md](architecture.md#agents).

- **Tabs** (WOR-0150): All plus one per board above the list; a click filters, Ctrl+click combines, and agents with no card show under All.
- **Header**: shows the model profile picked and why; a card agent's header has ↗ WOR-xxxx to show its card.
- **Needing you**: a "!" pip on the rail and the project tab means an agent needs you.
- **Saving**: agents are saved after every turn and resume after a restart; × forgets one.
- **Dictation**: the mic by the message box dictates at the caret (WOR-0118).
- **Several repos**: the Agents box and the Terminal toolbar show a repo picker when the project has several repos.

## Flows

Multi-agent pipelines, one markdown file each: `# Name`, `Max rounds: n`, a `## Role` section per stage. A stage whose brief starts `Sends back to: Role` is a checker ending with `VERDICT: PASS|FAIL`; any stage can end with `FLOW: STOP`.

- **Where they live**: shared flows are in the instance's `flows/`; a project's own are in its `.darg/flows/` (shown as "Name · this project"). The starter `designer-engineer-qa.md` is written the first time.
- **Running one**: from a card (context menu, panel) or the Flows section. Each stage is an agent handed the previous stages' last reply, fully automatic, and a run lasts only while the app does. Running one switches to [Agents](#agents).
- **Making one** (WOR-0077): the **+** generates a new flow from what you type (basic or art, this project or shared); an art flow follows the project's art skill by name, uses Forge and ends in an image review.

## Reports

Finished-agent reports, newest first, unread dotted and counted on the rail. When agents and Darg write them, and their format, is in [cards-and-qa.md](cards-and-qa.md#reports-darg-writes-itself).

- **Kinds**: a kind filter, shown as tabs, each with its count: All reports, Finished tasks, Investigations (including CNR), QA reports.
- **Reading**: Mark unread, Mark all read, Unread only, Show card; commits link to GitHub. Reports show markdown pictures and links.
- **Clean up**: archives QA reports of Done cards and writes any missing done report.
- **Stories**: the Story dropdown and Archive story move finished reports to `reports/archive/<story>/`; Archive (n) browses them and Restore brings them back. QA and undecided decision reports are never archived.
- **Decisions**: a pending decision report offers ✓ Approve and ↩ Send back.

## Artifacts

Pages agents made, shown in a web view. How they're served and how `darg.send()` and image reviews work is in [architecture.md](architecture.md#artifacts). A native web view is a native window, so nothing Avalonia draws (Fettle, popups) shows over it.

## Mistakes

Each project's `mistake_log.md`, newest first, with category counts; a category at three entries is marked red, because three of a kind is when a runtime gate beats another rule (WOR-0037). Darg only reads it; agents write it (every project agent's briefing tells it to log corrections). ✗ Mistake beside ✎ Draw tells the agent to log its last reply. The file name matters: the global unlogged-mistake hook matches `mistake_log.md`.

## Context

What an agent in this project starts with (☰ on the rail): the `CLAUDE.md` and `CLAUDE.local.md` files Claude Code loads (user's, each folder down to the repo, other repos'), the project's Claude memory files and Darg's default context. The picked file is drawn as markdown; Open sends it to its app. The user's `~/.claude/philip/*` detail files and skills are not listed (a choice, WOR-0145).

## Code

Each repo as a tree of git's file list, a finder, Changed only, tabs, syntax colouring (TextMate), git status. Editing is supported: type, ● marks an edited tab, Ctrl+S saves, closing an edited tab warns once. The editor can't be driven through Windows' accessibility interface, so typing in it has only been tried by hand.

## GitHub

Everything GitHub shows about the project's repos, so the browser isn't needed (WOR-0151). A tab per repo when there are several, then a tab per kind: Overview, Actions, Pull requests, Issues, Commits, Branches, Releases, Notifications. Each kind is a list with the picked one in full beside it.

- **Actions**: a run's jobs and steps, GitHub's annotations (failures first) and the failed job's log cut to the lines that say why, with the whole log a click away.
- **Pull requests and issues**: a PR's checks, files and conversation in order; an issue's comments.
- **Commits, releases, notifications**: a commit's diff; a release's notes and assets; a notification opens what it's about.
- **Cards**: an issue, a PR or a failed run (with its log excerpt) becomes a card with → card.
- **Writes**: ✓ Mark read is the only one; the agent runs all git and Darg never merges.
- **Accounts**: calls go through `gh` with the project's own account's token (set in `GH_TOKEN` for that call; the active gh account is never switched, and a private repo seen with the wrong account answers 404). One GraphQL call per repo; details are read when picked and kept until ↻ Refresh.
- **GitLab**: GitLab repos fill the tabs they have and need `glab` installed (untested here).

## Preview

The Site section: a web view of the project's dev server, from `preview` in `instance.json`, else the first repo with a `dev` script in `package.json`. The server runs in its own job so it stops with the app.

## Terminal

Consoles as tabs. `Shells` finds PowerShell 7, Windows PowerShell, Git Bash, cmd and each WSL distro (but not Docker Desktop's); + Shell opens the instance's `shell`, and picking one from ▾ opens it and makes it the default. Saved commands (set in [Setup](#setup) as `Name: command` lines) show as ▶ buttons and always run in the platform's first shell (PowerShell on Windows).

## Tools

The header's 🔧 Tools: status and start for Forge, ComfyUI, Ollama and Pinokio. Forge draws from a prompt, Ollama answers with a chat model, ComfyUI's newest images show. Start launches through the shell so the tool outlives Darg. Ollama's 14B models don't fit on the GPU while Forge holds SDXL; 7B ones do.

## Settings

The header's 🎨 Settings.

- **Themes**: a card per theme (Dark, Light, Girlypop), saved in `settings.json`. How themes work is in [architecture.md](architecture.md#themes).
- **Phone** (AND-0002): 📱 Pair a phone shows a QR and a code; each paired phone has a Revoke button. How the phone connects is in [REMOTE.md](REMOTE.md).
- **Phone notifications** (AND-0006): ✦ New topic sets up an ntfy topic, 🔔 Send test sends one, Turn off stops them. What makes a desktop toast also goes to the phone, and a card's decision carries its options as buttons.

## Setup

The instance's projects, per-project features as tick boxes, folders, repos, saved commands, commit rules, boards and docs. Saving rebuilds only the projects that changed. A project's commit rules map a folder to `push`, `commit`, `ask` or `never` as `rule folder` lines; the deepest folder holding the work wins, and agents are told the rule.

## Fettle

The corner mascot, one Claude session for the whole instance ([architecture.md](architecture.md#fettle)).

- **Tabs**: Chat and Notifications are tabs under its header; a count pip shows when it is closed.
- **Transcript**: tool steps are single dim lines with output folded.
- **Changes**: ask for a described change ("X should do Y") and it makes a Backlog card, replying with a link to it; "just fix it" edits (WOR-0132). When you are looking at an agent it asks "New ticket" or "Amend the agent's task" first (WOR-0086).
- **Leaving a chat**: a chat left mid-turn, by + New chat or picking another, finishes its turn and replies (WOR-0157).
