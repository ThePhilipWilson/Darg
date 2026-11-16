# Workbench

Philip's one app for everything: per-project boards, notes, docs, code, agents, consoles and tools. Native Avalonia (C#, .NET 10), one codebase for Windows, Mac and mobile. The plan and decisions live in the vault: `../vault/Code/Workbench/PLAN.md` (mockup beside it).

## Layout

- `src/Workbench.Core`: no UI. `InstanceConfig` (an instance's projects, in `%APPDATA%/Workbench/<instance>/instance.json`; `WORKBENCH_INSTANCE`, `WORKBENCH_HOME`), `BoardStore` (boards in Tatty's markdown format, so Tatty and Obsidian see the same files), `AgentSession` (a real Claude Code session run headless over stream-json), `ChildProcesses` (everything Workbench starts dies with it).
- `src/Workbench.Desktop`: the Avalonia app. MVVM with CommunityToolkit.Mvvm; views in `Views/`, view models in `ViewModels/`. Every project's view stays alive across tab switches, so agents and terminals keep running.
- `tests/Workbench.Core.Tests`: xunit. `WORKBENCH_LIVE=1` also runs the live agent test (a Haiku turn through the real CLI).

## Agents: the protocol

`claude -p --input-format stream-json --output-format stream-json --verbose --permission-prompt-tool stdio`. The `--permission-prompt-tool stdio` flag is what routes permission prompts to the host as `control_request` / `can_use_tool` **and** what makes `AskUserQuestion` available at all (without it the tool is missing from print mode). A question is answered by allowing it with `answers` (question text to label) added to its input. Permissions otherwise follow the person's own Claude settings: an edit their settings allow runs without a prompt, as in the terminal. Verified on Claude Code 2.1.325, 2026-11-16.

## Building and checking

- `NUGET_PACKAGES=D:/nuget/packages` (C: is small). `dotnet build`, `dotnet test`.
- See it without clicking: `WORKBENCH_SECTION=Board|Agents|Terminal` opens that section of the first project; `WORKBENCH_AGENT="prompt"` starts an agent there (in `WORKBENCH_AGENT_CWD` if set). `tools/shot.ps1 -ProcessId <pid> -Out x.png` captures only the app's window (PrintWindow, works when covered). UI Automation can press its buttons (Avalonia exposes them by name).
- Scratch output goes in `.shots/` (ignored).
