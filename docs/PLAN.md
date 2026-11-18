# Workbench (working name)

One app for everything Philip does across projects: docs, tickets, notes, code, agents, servers, art tools. AI everywhere. Everything except running Unity happens in here.

Started 2026-11-16 from Philip's freestyle, after trying Tim's Tatty. Tatty is good, but making it fit this would stop it being what Tim wants, so this is a separate build. Mockup: [mockup-v1.html](mockup-v1.html), opened locally in a browser.

## What it has to do

Philip's list, sorted. Each line is his ask, not a design decision yet.

### Projects
- Several projects open at once, as **tabs across the top** (not separate windows as in Tatty). Alpha, Beta, Project C and work are the first four.
- Each project has its own boards, notes, docs, repos, agents, consoles and accounts.

### Tickets (the "Jira-ish" part)
- Jira-style boards: several per project, filterable (labels, deity, source, agent running). What works for Alpha now is the model.
- Tickets and work in progress **move out of Obsidian**. Obsidian goes back to pure documentation.
- Confluence-style work-in-progress pages live here too, beside the tickets. (Need to pin down: are these the "website" docs below, or a third thing?)
- **Jira sync for work:** pull issues in, work on them, push status and comments back, while keeping local tickets alongside. Not a Jira replacement: smaller, local, can talk to Jira when needed.
- GitHub and GitLab issues, PRs and MRs show on the board too.

### Notes
- A **post-it wall**: kanban-style, filterable by tags and colour. Looser and quicker than tickets. A note can be turned into a ticket.

### Docs
- Read and edit the Obsidian vault from inside the app. Tickets link back to the doc and heading they came from.
- The **website-style documentation** Philip builds for work projects (his own sites that pad out the docs). Examples will come from his work PC.

### Code
- Browse code across **several repos per project**, with a git view (branch, changes, PRs and MRs).
- **GitHub and GitLab per project, with different accounts.** At least three profiles: work, home and game. Each project says which profile it uses for each service, and a repo can override that.

### Agents (the herdr part)
- herdr-style: many agents running at once, click through to any one.
- **Full Claude Code richness, not a terminal**: inline image previews, code and diff previews, files opened or edited shown directly. herdr can't do this from the CLI; Philip doesn't want to open Claude Code just to see things.
- **OS notifications on Windows and Mac**: clicking one opens *that agent at what's happening*, not a list of agents.
- **Artifacts run locally**, served by the app on the local network. Never Claude's cloud artifacts: work rules forbid putting work content in the cloud.

### The mascot
- A little character with a text box, available anywhere in the app and in any project. It can run things and edit anything ("split every deity card and tag them"; "what's waiting on me?").

### Run
- Consoles inside the app: dev servers, sims, the work stack (Tilt and the like), all self-contained.
- Pinokio, Ollama and ComfyUI driven from inside, with ComfyUI's view visible and usable.

### Not only engineering
- Design, art, planning and engineering all from here. The claude-draw plugin is used for drawing on things.

### Phone and tablet
- A mobile version on the local network, portrait and landscape.
- **Remote by default**: no "turn on remote" step. It tunes into every open project automatically.

## What Tatty taught us (keep)
- **Plain files for cards.** A ticket is a markdown file in a folder, so git and search work on it for free. Now in a non-Obsidian folder.
- **Real Claude Code sessions, not the raw API.** Hooks, skills, rules and plugins come along. The rich view needs the session's structured output, not its terminal (see below).
- **The backlog importer and the card tool** (`import-tasks.py`, `tasks.py` in the Tatty fork) are proven code to start from.
- Tatty's multi-window model is what Philip *doesn't* want; tabs instead.

## The big technical calls

These decide most of the build. My recommendation first.

1. **How agents are shown.** herdr hosts terminals, which is why it can't show images inline. To get Claude Code's richness, run each agent through Claude Code's structured stream (the Agent SDK, or `claude` with JSON streaming in and out) and draw the conversation ourselves: tool calls become image previews, diffs and file views. Keep "Open in terminal" as an escape hatch. *Recommended: structured stream as the main view.* Still to check this session: whether skills, plugins and AskUserQuestion behave identically in that mode.
2. **App shell: decided 2026-11-16, Avalonia (C#).** A standalone native app, not a web app; the same codebase for Windows, Mac, iOS and Android, so there's one version. Drawn natively. HTML is the exception, not the base: interactive HTML artifacts and doc-site previews go in Avalonia's native web view or open in the system browser. ComfyUI and Forge are driven through their APIs (queue a workflow, watch progress, show images natively); ComfyUI's own graph editor isn't needed. Code view: AvaloniaEdit. Weakest part to test early: an embedded terminal (needs a terminal library). Still to check: whether Avalonia's web view is in the free tier. Considered: Tauri 2 (HTML screens), Slint (Rust, fewer parts), Compose Multiplatform (Kotlin), Flutter (Dart), Electron (no phone).
3. **Where tickets and notes live.** A folder per project outside the vault (for example `D:\GitHub\example-studio\<project>-work\` or inside each repo), markdown per card, pushed to a private repo. Work projects keep theirs on the work PC only.
4. **Phone access.** The phone and tablet run the same Avalonia app, talking to the desktop over the LAN with one-time pairing (QR code) and a token per device. Remote is on by default, pairing happens once.
5. **Accounts.** Profiles (work, home, game) for GitHub, GitLab and Jira, with tokens in the OS keychain. Each project names a profile per service, and a repo can override. git operations use that profile's credentials.

## Decided (2026-11-16)
- **WIP pages and doc sites are two things.** Rough Confluence-style pages sit beside tickets; the doc sites are polished output built from them.
- **The mascot lives inside the app only**: in every project tab and on the phone, not floating over other apps.
- **One app, run as instances, everything opt-in like a shopping list.** No separate work build. Every install has the same tick boxes, so you run only what you need:
  - **Per instance** (one install, with its own data folder): tick which projects it includes. The work PC's instance ticks only work projects; home ticks Alpha, Beta, Project C and the rest.
  - **Per project**: tick which features it uses. A work project might tick board, docs, code, agents, Jira and GitLab, and leave the phone, Forge, ComfyUI and Pinokio unticked.
  - An unticked feature doesn't start, holds no connection and doesn't show in the rail. Instance settings can also lock a feature off for every project in it (e.g. "no phone access on this machine"), for the things work won't approve.
- **Instances can't see each other.** The work instance has no view of home: no shared projects, notes, accounts, settings or sync. If something ever needs to cross (a skill, a board template), it's copied over by hand on purpose.
- Features on the list so far: board, notes wall, docs, WIP pages, doc sites, code, agents, consoles, phone and tablet, Forge, ComfyUI, Pinokio, Ollama, claude-draw, GitHub, GitLab, Jira, mascot.

## Open questions (for Philip)
- What's it called?

## Built

**Repo:** `ThePhilipWilson/Workbench` (private; moved from the studio account 2026-11-17, history rewritten so it carries no studio name; the old repo is archived). Code at `D:\GitHub\ThePhilipWilson\Workbench`.

**Slice 1 (2026-11-16):** project tabs (each stays alive), the board on the existing Tatty files (filters by text and label, drag and drop, Move to), agents as real Claude Code sessions with images, diffs, write previews, questions and permission prompts drawn inline, and a PowerShell terminal per project. Everything Workbench starts dies with it (a Windows job object). The two riskiest parts are proven: the rich agent view (`--permission-prompt-tool stdio` is the key: it also turns on AskUserQuestion) and the embedded terminal (Iciclecreek.Avalonia.Terminal).

Known gaps: no OS notifications yet, assistant text isn't rendered as markdown, a permission card has only been tested through the question path (Philip's settings allow edits, so none appeared), and drag and drop hasn't been tried with a real mouse.

**Slice 2 (2026-11-16):** Windows notifications when an agent has a question, wants a tool or finishes (only when you're not looking at it), each opening that agent when clicked; "Work on" on a card starts an agent with the card, moves it to In Progress, and the card shows the agent live; the notes wall (post-its as markdown with Obsidian properties, columns, colours, tags, filters, drag, Make it a ticket); Bench, the mascot, one session across every project that edits boards and notes directly.

Still untested by a real hand: clicking a toast, dragging cards and notes, typing in the terminal, opening an existing note to make it a ticket.

**Slice 3 (2026-11-16, built while Philip was on his phone):** markdown in agent and Bench replies (own small renderer: headings, lists, tables, code blocks, quotes); + Card and a card panel (click a card to read and edit it, Work on this from there); the code view (each repo as a tree of git's file list, a finder, Changed only, tabs, syntax colouring, read-only, git status); Setup (the instance's projects, each project's features as tick boxes, folders and repos; saving rebuilds only changed projects).

Also built: GitHub per project, read-only: open PRs, issues (one click makes a card) and recent CI runs per repo, through gh with the project's own account (Alpha, Beta, Project C set to example-studio).

Also built: the Tools page (🔧, top right): Forge, ComfyUI, Ollama and Pinokio status; Forge draws from a prompt; Ollama answers with a chat model; ComfyUI's newest images; Start launches a tool through the shell so it outlives Workbench. Learned: Ollama's 14B models don't fit on the GPU while Forge holds SDXL; 7B ones do.

**Overnight 2026-11-16/17:** agents survive restarts (saved per project, resumed with --resume); consoles as tabs with saved commands; the Docs view on the vault; editing and saving in the code view; GitLab beside GitHub; a published copy in `D:\Apps\Workbench` with shortcuts; view crashes logged instead of closing the app. What to try by hand: [MORNING.md](MORNING.md).

## Next
The phone and tablet ([PHONE-PLAN.md](PHONE-PLAN.md), three questions first); Jira for the work instance; starting Forge and ComfyUI from Workbench. Philip also sends the work doc-site examples.
