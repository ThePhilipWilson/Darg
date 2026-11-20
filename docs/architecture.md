# Architecture

## Two projects, one rule

- `src/Darg.Core`: no UI. Everything that can be tested without a window: stores for boards, notes, reports, mistakes, flows and artifacts, the agent session, the local API, git and GitHub helpers, themes and settings.
- `src/Darg.Desktop`: the Avalonia app, MVVM with CommunityToolkit.Mvvm. Views in `Views/`, view models in `ViewModels/`. Every project's view stays alive across tab switches, so agents and terminals keep running.
- Tests: `tests/Darg.Core.Tests` (unit) and `tests/Darg.Desktop.Tests` (integration, the real window headless). See [getting-started.md](getting-started.md).

Rule of thumb: put behaviour in Core with a unit test; keep the view model thin. `ColumnWatch` and `ReportFlow` (what happens when a card moves) are in Core for exactly that reason, not in the view models (WOR-0102).

## Instances and projects

An instance is a data folder (`%APPDATA%\Darg\<instance>`, or `DARG_HOME`). `instance.json` lists its projects; each project has features (sections) switched on, folders and repos, boards, docs, saved commands, commit rules and an optional `preview`. `LastProject` remembers the open tab in `last-project`, kept apart from `instance.json` so switching tabs doesn't rewrite a file people edit. Instances cannot see each other.

Beside `instance.json` the instance folder also holds: `settings.json` (theme), `usage.json` (latest usage readings), `profiles.md` (model profiles), `flows/` (shared flows), `fettle-briefing.md`, `crash.log`, `stalls.log`, `dictation.log`, and the saved agents.

## Where a project's files live

- **Boards and notes**: in Tatty's markdown format, so Tatty and Obsidian see the same files. A board is a folder with `board.json` (column order, minimized lists, an `attentionColumn` flag) and `items/*.md`, one file per card with a `- **Status:**` list and sections. Boards set elsewhere (the vault's Tatty) stay where they are.
- **Working files in the repo** (WOR-0144): `.darg/` at the top of the project's first repo holds `reports/`, `mistakes/`, `artifacts/`, the project's `flows/`, `shown-boards`, `column-sorts`, and `tasks/` and `notes/` when Darg made them. `.darg/` is added to `.git/info/exclude` the first time it is made; delete that line to commit it. A project with no repo on the machine keeps `<instance>/projects/<id>/`. Always go through `ProjectData.Root` (`ReportStore.FolderFor`, `MistakeLog.PathFor`, `ArtifactStore.Folder`, `FlowStore.ProjectFolder`), never build the path by hand. `ProjectData.Migrate` moves old folders in at start and the footer says what moved.
- **Agents work in worktrees; `.darg/` is in the main checkout only.** Give agents absolute paths.
- **Docs** live in the repo's `docs/` by default (decided 2026-11-18, WOR-0123). Darg only reads docs; there is no junction back to an Obsidian vault. `ProjectDetect` prefers a repo's `docs/` over the vault's `Code/<name>`. `[[wikilinks]]` that need the vault show as missing in repos. Darg reads `instance.json` only at start, so a moved docs path needs a restart.

## Agents

An agent is a real Claude Code session run headless:

```
claude -p --input-format stream-json --output-format stream-json --verbose --permission-prompt-tool stdio
```

`--permission-prompt-tool stdio` is the key flag. It routes permission prompts to the host as `control_request`/`can_use_tool` and it is also what makes `AskUserQuestion` exist in print mode at all. A question is answered by allowing it with `answers` (question text to label). Otherwise permissions follow the person's own Claude settings.

- `AgentSession` (Core) runs one. It queues what it sends (process starts and stdin writes stay off the UI thread). `AgentStore` saves agents after every turn so they survive restarts (resumed with `--resume`); `SaveLater` keeps saving off the UI thread.
- `ChildProcesses` puts everything Darg starts in a Windows job object, so it dies with Darg. Dev servers start in their own job (`ChildProcesses.Group`) because a tree kill left npm's node running. The Updater starts outside the job on purpose.
- Every agent has a **purpose** (work, qa, improve, rework, flow, chat), saved with it (older saves have none). Purpose decides things like whether Run QA shows (WOR-0110) and whether a rework agent triggers Run QA when its card enters QA (WOR-0115).
- Every agent's job is an `AgentJob`; **model profiles** pick its model (`ModelProfiles`, `profiles.md`): the first matching rule (`kind|type|label|role|project: value -> Profile`), else Haiku picks from the `Use for:` lines, else `Otherwise:`, else the person's own settings. A model or effort picked by hand meanwhile wins.
- A **closed project starts no agent**: one still picking its model doesn't start, a new one isn't started, and a message doesn't resume an ended one (WOR-0131).
- "Out of usage" shows only for a real limit: a `rate_limit_event` with status `rejected`, or an errored turn whose text matches (`UsageLimit.StoppedByLimit`; a reply that merely quotes "usage limit" doesn't count, WOR-0093).
- Briefings added to every project agent through `--append-system-prompt`: `Decisions.Briefing` (questions go through AskUserQuestion, never prose), `ArtifactStore.Briefing`, the commit rule for where it works, the mistake log, `ReportStore.Rules`. These briefings go on a command line, so no `<>"&|` in them. Fettle's is too long for cmd's 8191-character limit, so it is `fettle-briefing.md` in the instance folder, passed with `--append-system-prompt-file` (WOR-0138).
- A card agent works on a new branch named for the card (`wor-0035`) in a worktree of its own and fast-forwards it in when done, unless the card names an existing branch (`Branch: name`, "continue on branch name", or a `branch:name` label). This is only prompt text (`CardBranch.Rules`); the agent runs all the git.
- A project with several repos: agents and consoles start in the first repo unless told otherwise (a card names its repo with `repo:Name` or a `Repo: Name` line, `ProjectConfig.RepoFor`) and every agent gets the others with `--add-dir`.

## Fettle

Fettle is the corner mascot (once called Bench): one Claude session for the whole instance with `--add-dir` for every project's boards, notes and repos, and a briefing on the file formats and the API. Its rules live in `FettleRules`; its context line `[Philip is looking at: …]` says which project, section, agent, card, column and file you are on (`MainViewModel.Where`). It can be dragged anywhere (a press moving under 6 px is a click), but only from the title and tab rows.

## The Darg API

`Api.cs` and `MainViewModel.Api.cs`: commands the app registers in `ArtifactServer.Api` for what isn't a file edit. `GET /api/<token>` lists them with usage lines; `POST /api/<token>/<name>` with a JSON body (or query string) runs one; an `ApiError` returns 400 with its message. Agents get the address as `DARG_API`: `curl -s -X POST "$DARG_API/cards" -d '{"project":"x"}'`.

What it covers: show a card, doc, section, agent or artifact; `cards`/`card` (list and read across boards, joined with agents: `column` takes a name or `qa`/`attention`/`done`, `agent` is `none`/`live`/`any`); `overview` (column counts, working agents, newest artifacts, unread reports, per-repo branch and recent commits); `new-card`, `work-on`, `work-all`, `qa-all`, `improve-card`, `send-back`, `decide`, `to-attention`; list/read/start/message/close agents; run flows and saved commands; `new-flow`; `theme`. A new command is usable by Fettle the moment it is registered, so give it a usage line that says what it takes.

Why it exists (WOR-0135): over 44 Fettle sessions it fumbled on making cards (4 to 6 calls each), finding the API, joining card state with agents and "why isn't this button showing", and never on notes, reports, docs, mistakes or flows. The commands above target exactly those.

## Artifacts

HTML pages agents make, kept in `<project data>/artifacts/<agent id>/`, never in Claude's cloud (work rules forbid work content in the cloud). `ArtifactServer` serves them on `http://localhost:47817/` (any free port if taken) and injects a `darg.send()` bridge: a send POSTs to a URL carrying the instance's token (`artifact-token`) and reaches the agent that made the page as a message starting "From artifact". `/file/<absolute path>` serves images only. An image review is a `*.review.json` (title, note, folder, rows of id / original / candidates) that Darg shows in its own picker (`Review.html`, embedded in Core); Send replies `id: file | note` per row, with `original` or `NEEDS PASS`.

## Themes

`Themes` in Core defines Dark, Light and Girlypop: a colour for every key in `Themes.Keys`. `ThemeManager` (Desktop) puts them in the app's resources. **Views use `{DynamicResource Key}`, never a hex or `StaticResource`.** A new colour needs a key in all three themes (a test checks they match). Code-built views read `ThemeManager.Brush(key)` and rebuild on `ThemeManager.Changed`. Post-it paper, Fettle and the terminal keep fixed colours. Avalonia's Fluent palette only takes Light/Dark keys and only a change of `Accent` reaches the controls live, so a theme can set `FluentAccent` and nothing else of Fluent's (in Girlypop, text on standard buttons and boxes stays near-black).

## Things that run beside the app

- **Usage bar** (WOR-0069): every turn's `rate_limit_event` carries `unifiedWindows` (five_hour, seven_day). `StreamParser` reads them, `AgentSession` reports them to `UsageMeter.Shared`, which keeps the latest in `usage.json`. The header bar is red from 80% and faded when the reading is over an hour old. Only Darg's own agents feed it. Beside it, `today 3h 12m` (`WorkTimer`) is Claude time since 04:00 read from claude-code-timer's timings in `~/.claude/stats/timings`, with parallel sessions merged; hidden when the timer isn't installed. Neither number has been checked against Anthropic's own usage page.
- **Notifications** (`Decisions`, WOR-0078): only an agent's question or permission ask, and the end of a turn of 15 minutes or more, notify. Windows toasts carry project and agent ids; clicking one opens that agent. Darg is the only notifier now: the old Claude Code toast hooks were removed from `~/.claude` (WOR-0119).
- **Voice**: dictation only, a mic button beside the Fettle box and the agent message box. It dictates at the caret and sends nothing. Two recognisers appear in the history: first Windows' offline `System.Speech` (poor accuracy untrained), then local Whisper `small.en` (GPU via Vulkan, CPU fallback; the ~470 MB model downloads once to `%APPDATA%\Darg\models` on the first mic click). It doesn't use Claude Code's own voice, which needs a claude.ai login. Status goes to `dictation.log`. Stopping the mic mid-phrase drops that phrase. (WOR-0118)
- **ClaudeDraw**: the claude-draw daemon on 7331 is shared by every Claude Code session; Darg only reads its `/state`, opens the canvas and starts a blank one. ✎ Draw sends the agent a message; agents use their own plugin tools.
- **Preview/DevServer** (Site section): a project's `preview` in `instance.json`, else the first repo whose `package.json` has a `dev` script.
