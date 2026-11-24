# Darg

Darg is one app for everything in a project: boards, notes, docs, code, agents, consoles and tools. It is a native Avalonia app (C#, .NET 10) with one codebase aimed at Windows, Mac and mobile. Its agents are real Claude Code sessions run headless.

## Build and run

You need the .NET 10 SDK.

```
dotnet build
dotnet test
```

On Windows the app is `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe`. Run `dotnet build` before launching it. `dotnet test` only rebuilds Core and the tests, not the app.

If restore fails looking for a Visual Studio fallback package folder, use the `NuGet.config` in the repo root. Set `NUGET_PACKAGES` if your package cache lives somewhere other than the default.

Useful settings:

- `DARG_HOME` points the app at a different instance folder, such as a throwaway one for testing.
- `DARG_SECTION=Board|Agents|Preview|Terminal` opens that section on start.
- `DARG_LIVE=1` makes the tests also run a live agent test through the real Claude CLI.

To install a copy you use day to day, run `pwsh -File tools/publish.ps1`. It builds Release into `D:\Apps\Darg` and adds shortcuts. Don't run it from inside a Darg agent, because it closes the running copy.

## Layout

- `src/Darg.Core`: everything without UI. It holds the instance config, board and notes stores, agent sessions, artifacts, flows, model profiles, reports, mistake log and the local API.
- `src/Darg.Desktop`: the Avalonia app, in MVVM style. Views are in `Views/` and view models in `ViewModels/`.
- `tests/Darg.Core.Tests`: xunit tests for Core.
- `tools/`: `publish.ps1` installs a build, and `shot.ps1` captures the app window to a PNG.
- `CLAUDE.md`: the detailed notes on how each part works.

## Licence

MIT, see [LICENSE](LICENSE).
