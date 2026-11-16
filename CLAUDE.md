# Workbench

Philip's one app for everything: per-project boards, notes, docs, code, agents, consoles and tools. Native Avalonia (C#, .NET 10), one codebase for Windows, Mac and mobile. The plan and decisions live in the vault: `../vault/Code/Workbench/PLAN.md` (mockup beside it).

## Layout

- `src/Workbench.Core`: no UI. `InstanceConfig` (an instance's projects, in `%APPDATA%/Workbench/<instance>/instance.json`; `WORKBENCH_INSTANCE`, `WORKBENCH_HOME`), `BoardStore` (boards in Tatty's markdown format, so Tatty and Obsidian see the same files), `AgentSession` (a real Claude Code session run headless over stream-json), `ChildProcesses` (everything Workbench starts dies with it).
- Also in Core: `NoteStore` (the notes wall: one .md per note with Obsidian properties; `MakeTicket` uses `BoardStore.Create`, which names files as Tatty does).
- Desktop extras: `Notifier` (Windows toasts carrying project and agent ids; clicking opens that agent; the Desktop project targets `net10.0-windows10.0.19041.0` when built on Windows, plain `net10.0` elsewhere, and code for toasts sits under `#if WINDOWS`), `MascotViewModel` (Bench: one session for the instance with `--add-dir` for every project's boards, notes and repos, and a briefing on the file formats via `--append-system-prompt`). Transcript templates live in `App.axaml`'s `Application.DataTemplates`, shared by the agents view and Bench.
- `src/Workbench.Desktop`: the Avalonia app. MVVM with CommunityToolkit.Mvvm; views in `Views/`, view models in `ViewModels/`. Every project's view stays alive across tab switches, so agents and terminals keep running.
- `tests/Workbench.Core.Tests`: xunit. `WORKBENCH_LIVE=1` also runs the live agent test (a Haiku turn through the real CLI).

## Agents: the protocol

`claude -p --input-format stream-json --output-format stream-json --verbose --permission-prompt-tool stdio`. The `--permission-prompt-tool stdio` flag is what routes permission prompts to the host as `control_request` / `can_use_tool` **and** what makes `AskUserQuestion` available at all (without it the tool is missing from print mode). A question is answered by allowing it with `answers` (question text to label) added to its input. Permissions otherwise follow the person's own Claude settings: an edit their settings allow runs without a prompt, as in the terminal. Verified on Claude Code 2.1.325, 2026-11-16.

## UI rules learned from Philip's use

- **A list must read as a list of things, not of times or cut-off text.** Chats and agents went out named "Bench · 11:00" and the first 40 characters of a prompt; it annoyed him on sight (2026-11-17). When a screenshot shows a list, check that a stranger could tell its rows apart and say what each is; generated items get a real title (`ChatTitle`).
- **Structure on disk shows as structure on screen.** A folder of docs or files is a tree that expands, not a flat list of every file (2026-11-17).
- **Drag and drop shows the thing moving** (`DragLift`), never the OS drag with nothing under the pointer.

## Building and checking

- `NUGET_PACKAGES=D:/nuget/packages` (C: is small). `dotnet build`, `dotnet test`. `dotnet test` rebuilds only Core and the tests, not the app: run `dotnet build` before launching the exe to check a change, or you're looking at the old build.
- GitHub calls go through `gh` with the project's account's token (`gh auth token --user <account>`) in `GH_TOKEN` for that call only; the active gh account is never switched. A private repo seen with the wrong account answers 404.
- See it without clicking: `WORKBENCH_SECTION=Board|Agents|Terminal` opens that section of the first project; `WORKBENCH_AGENT="prompt"` starts an agent there (in `WORKBENCH_AGENT_CWD` if set). `tools/shot.ps1 -ProcessId <pid> -Out x.png` captures only the app's window (PrintWindow, works when covered). UI Automation can press its buttons (Avalonia exposes them by name).
- **The copy Philip uses** is published: `pwsh -File tools/publish.ps1` builds Release into `D:\Apps\Workbench` (closing a copy running from there first; agents are saved after every turn) and makes Start menu and desktop shortcuts. Debug builds in `bin/` are for checking.
- A crash in a view goes to `crash.log` in the instance folder instead of closing the app; read it after anything odd.
- Scratch output goes in `.shots/` (ignored). For anything that writes cards or notes, point the app at a throwaway instance (`WORKBENCH_HOME=.shots/home`, with its own `instance.json` and board), never at the real vault boards.
- The exe is `src/Workbench.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Workbench.Desktop.exe` on Windows.
