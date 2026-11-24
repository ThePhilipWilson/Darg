# WOR-0192: Open md links from agent transcripts in the Docs viewer

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-23
- **Done:** 2026-11-23
- **Moved:** 2026-11-23 12:04:48

## Description

> When Philip clicks a link to an md file (e.g. docs/Work/BACKLOG-BOARDS-PROPOSAL.md, as seen in an agent transcript), it should open inside Darg in a markdown viewer rather than elsewhere. Seen from Alpha > Agents > "Backlog split proposal".

A link to a .md file in an agent transcript (or Fettle's replies) opens in Darg's Docs section, in the markdown viewer, instead of Windows' default app. Web and mail links and other file types open in their own apps as now.

**Where:** `src/Darg.Desktop/Views/MarkdownView.cs`, `Open` (about line 270): with no `LinkClicked` handler (transcripts, Fettle) it calls `Process.Start`. `DocsViewModel.OpenLink` (`ViewModels/DocsViewModel.cs`, about line 210) with `Core.DocLinks.Resolve` already shows a .md inside or outside the docs folder, and `OpenElsewhere` hands it to the project whose docs hold it. Wire it the way `MarkdownView.OpenCard` is (a static hook set in `MainWindow.axaml.cs`), resolving a relative path against the agent's working folder (the example is under Alpha's repo) and switching to the right project's Docs section. Integration test in `tests/Darg.Desktop.Tests`.

**Done when:**
- Clicking a .md link (relative, e.g. `docs/Work/BACKLOG-BOARDS-PROPOSAL.md`, or absolute) in an agent transcript switches to that project's Docs section showing the rendered file.
- A .md outside the project's docs folder (another repo) still opens, in the project whose docs hold it, else in Docs as "Outside the docs folder".
- A missing file says so (Docs status line) rather than failing silently.
- http(s), mailto and non-.md links in a transcript still open in their own apps.
- An integration test fails without the change; `pwsh -File tools/test.ps1` passes.
